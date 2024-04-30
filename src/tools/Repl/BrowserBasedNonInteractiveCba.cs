//This code is taken from the below repository
//https://dev.azure.com/msazure/One/_git/Flow-CBA?version=GBmain&path=/src/BrowserBasedNonInteractiveCba.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ----------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ----------------------------------------------------------

namespace Microsoft.PowerFx
{
    /// <summary>
    /// CBA non interactive.
    /// </summary>
    public class BrowserBasedNonInteractiveCba : IDisposable
    {
        private readonly HttpClientHandler InternalHttpClientHandler;

        private readonly HttpClient InternalHttpClient;

        private struct AuthFlowContext
        {
            public string Ctx { get; set; }
            public string FlowToken { get; set; }
        }

        public BrowserBasedNonInteractiveCba()
        {
            InternalHttpClientHandler = new HttpClientHandler()
            {
                AllowAutoRedirect = false,
            };

            InternalHttpClient = new HttpClient(InternalHttpClientHandler);
        }


        /// <summary>
        /// Get an access token using Certificate-based authentication.
        /// 
        /// NOTE: This is done by simulating the user login flow using a certificate, as done by the browser.
        /// It will fetch and parse the HTML content from the login.microsoftonline.com endpoint. Is is subject to break in case
        /// of changes in the format returned by the endpoint.
        /// 
        /// NOTE2: This code was originally developed as a proof-of-concept for Power Automate Desktop support for certificate-based authentication.
        /// Because it relies on parsing HTML content, it is not recommended for production use, and is not the method Power Automate Desktop uses to
        /// perform CBA. Instead, if the application is running on an AAD-joined Windows device, it can use the protocol described in MS-OAPXBC to 
        /// perform CBA: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-oapxbc/6da94507-af2e-41ed-a8da-e6edd4cbc2ca
        /// </summary>
        /// <param name="authority">Authority.</param>
        /// <param name="clientId">clientId.</param>
        /// <param name="scope">Scope.</param>
        /// <param name="redirectUri">RedirectUri.</param>
        /// <param name="tenantId">Tenant of the user.</param>
        /// <param name="username">Username.</param>
        /// <param name="certificate">Certificate to be used for the user authentication.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
        public async Task<string> GetTokenWithCertificateSilentlyAsync(
            string authority,
            string clientId,
            string scope,
            string redirectUri,
            string tenantId,
            string username,
            X509Certificate2 certificate)
        {
            return await InternalGetTokenWithCertificateSilentlyAsync(
                authority,
                clientId,
                scope,
                redirectUri,
                username,
                certificate);
        }

        public void Dispose()
        {
            InternalHttpClient?.Dispose();
        }

        //public void Reset()
        //{
        //    InternalHttpClientHandler.CookieContainer.GetAllCookies().ToList().ForEach(c => c.Expired = true);
        //}

        private async Task<string> InternalGetTokenWithCertificateSilentlyAsync(
            string authority,
            string clientId,
            string scope,
            string redirectUri,
            string username,
            X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            // Generate a code verifier/challenge pair as done by the msal-browser library.
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = GenerateCodeChallengeFromCodeVerifier(codeVerifier);

            var nonce = Guid.NewGuid().ToString();

            // Issue a initial request to the /authorize endpoint to get the login context.
            // This initializes the flowToken and ctx fields that have to be maintained throughout the auth flow. 
            AuthFlowContext authorizeContext =
                await GetLoginContextAsync(
                    $"{authority}/oauth2/v2.0/authorize" +
                        $"?client_id={clientId}" +
                        $"&scope={HttpUtility.UrlEncode(scope)}" +
                        $"&redirect_uri={System.Web.HttpUtility.UrlEncode(redirectUri)}" +
                        $"&response_mode=fragment" +
                        $"&response_type=code" +
                        $"&code_challenge={System.Web.HttpUtility.UrlEncode(codeChallenge)}" +
                        $"&code_challenge_method=S256" +
                        $"&nonce={nonce}").ConfigureAwait(false);

            // Retrieve the certificate authentication URL. This URL depends on the tenant.
            var (certAuthUrl, updatedAuthFlowContextAfterCertAuthUrlFetch) =
                 await GetCertAuthUrlAsync(
                     authority,
                     username,
                     authorizeContext).ConfigureAwait(false);

            // By issuing a request to the certAuthUrl with the user-certificate as an SSL-client cert, 
            // we get a certificatetoken (along with updated ctx and flowToken) to pass to the /login endpoint.
            var (certificateToken, updatedAuthFlowContextAfterCertAuthentication) =
                await AuthenticateWithCertificateAsync(
                    authority,
                    certificate,
                    certAuthUrl,
                    updatedAuthFlowContextAfterCertAuthUrlFetch).ConfigureAwait(false);

            // Get the auth code from the /login endpoint.
            var loginAuthCode = await LoginAsync(
                authority,
                certificateToken,
                updatedAuthFlowContextAfterCertAuthentication).ConfigureAwait(false);

            // Exchange the auth code for an access token.
            var token = await GetAccessTokenAsync(
                authority,
                clientId,
                scope,
                redirectUri,
                codeVerifier,
                loginAuthCode).ConfigureAwait(false);

            if (string.IsNullOrEmpty(token))
            {
                throw new Exception("Failed to get access token.");
            }

            return token;
        }

        /// <summary>
        /// PKCE challenge/verifier generation taken from AAD code.
        /// </summary>
        /// <param name="codeVerifier">Code verifier.</param>
        /// <returns>Code challenger.</returns>
        private static string GenerateCodeChallengeFromCodeVerifier(string codeVerifier)
        {
            // From https://msazure.visualstudio.com/One/_git/AzureUX-PublishingExperience?path=/src/PublishingPortal/Publishing.Website/node_modules/%40azure/msal-browser/dist/crypto/PkceGenerator.js&_a=contents&version=GBmaster .
            using (var sha256Hash = SHA256.Create())
            {
                return Convert.ToBase64String(
                    sha256Hash.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)))
                        .Replace("=", string.Empty)
                        .Replace('+', '-')
                        .Replace('/', '_');
            }
        }

        /// <summary>
        /// PKCE challenge/verifier generation taken from AAD code.
        /// </summary>
        /// <returns>Challenge and verifier.</returns>
        private static string GenerateCodeVerifier()
        {
            // From https://msazure.visualstudio.com/One/_git/AzureUX-PublishingExperience?path=/src/PublishingPortal/Publishing.Website/node_modules/%40azure/msal-browser/dist/crypto/PkceGenerator.js&_a=contents&version=GBmaster .
            // Generate a random string using a cryptographically secure random number generator.
            using (var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[32];
                rng.GetBytes(bytes);
                return Convert.ToBase64String(bytes)
                    .Replace("=", string.Empty)
                    .Replace('+', '-')
                    .Replace('/', '_');
            }
        }

        private async Task<string?> GetAccessTokenAsync(
            string authority,
            string clientId,
            string scope,
            string redirectUri,
            string codeVerifier,
            string authCode)
        {
            var body = new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "scope", scope },
                { "redirect_uri", redirectUri },
                { "grant_type", "authorization_code" },
                { "code_verifier", codeVerifier },
                { "code", authCode }
            };

            // Get the access token from the /token endpoint.
            using (var tokenRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{authority}/oauth2/v2.0/token"))
            {
                tokenRequest.Content = new FormUrlEncodedContent(body);
                var tokenResponse = await InternalHttpClient.SendAsync(tokenRequest).ConfigureAwait(false);

                var tokenResponseContent = await tokenResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                var tokenResponseJson = JObject.Parse(tokenResponseContent);
                if (!tokenResponse.IsSuccessStatusCode)
                {
                    throw new ArgumentException(tokenResponseJson["error_description"]?.ToString());
                }

                return tokenResponseJson["access_token"]?.ToString();
            }
        }

        // Returns a (certificatetoken, updated AuthFlowContext) tuple.
        private async Task<(string, AuthFlowContext)> AuthenticateWithCertificateAsync(
            string authority,
            X509Certificate2 certificate,
            Uri certAuthUrl,
            AuthFlowContext authFlowContext)
        {
            using (var handler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                UseDefaultCredentials = true,
            })
            {
                handler.ClientCertificates.Add(certificate);

                using (var httpClient = new HttpClient(handler))
                {
                    using (var certAuthRequest = new HttpRequestMessage(
                        HttpMethod.Post,
                        certAuthUrl))
                    {
                        certAuthRequest.Content = new FormUrlEncodedContent(
                            new Dictionary<string, string>
                            {
                                { "ctx", authFlowContext.Ctx },
                                { "flowToken", authFlowContext.FlowToken },
                            });
                        certAuthRequest.Headers.Connection.Add("keep-alive");
                        certAuthRequest.Headers.Add("Referer", authority);
                        certAuthRequest.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue()
                        {
                            NoCache = true,
                            NoStore = true,
                        };
                        var response = await httpClient.SendAsync(certAuthRequest).ConfigureAwait(false);
                        var htmlContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        var htmlDoc = new HtmlDocument();
                        htmlDoc.LoadHtml(htmlContent);
                        var hiddenInputs = htmlDoc.DocumentNode.SelectNodes("//input[@type='hidden']");
                        var parsedCertAuthenticationResult = new Dictionary<string, string>();
                        foreach (var input in hiddenInputs)
                        {
                            parsedCertAuthenticationResult.Add(
                                input.Attributes["name"].Value,
                                input.Attributes["value"].Value);
                        }

                        var updatedAuthFlowContext = new AuthFlowContext
                        {
                            Ctx = parsedCertAuthenticationResult["ctx"],
                            FlowToken = parsedCertAuthenticationResult["flowtoken"]
                        };

                        var certificateToken = parsedCertAuthenticationResult["certificatetoken"];

                        return (certificateToken, authFlowContext);
                    }
                }
            }
        }

        /// <summary>
        /// Get the certificate authentication URL from the login.microsoftonline.com endpoint.
        /// </summary>
        /// <param name="authority">Authority.</param>
        /// <param name="username">Username.</param>
        /// <param name="authConfig">AuthConfig.</param>
        /// <returns>Cert auth url.</returns>
        private async Task<(Uri, AuthFlowContext)> GetCertAuthUrlAsync(
            string authority,
            string username,
            AuthFlowContext authFlowContext)
        {
            using (var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{authority}/GetCredentialType?mkt=en-US"))
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(new
                    {
                        username,
                        flowToken = authFlowContext.FlowToken,
                    }),
                    Encoding.UTF8,
                    "application/json");
                var response = await InternalHttpClient.SendAsync(request).ConfigureAwait(false);
                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var jobject = JObject.Parse(responseContent);

                var updatedAuthFlowContext = new AuthFlowContext
                {
                    Ctx = authFlowContext.Ctx,
                    FlowToken = jobject["FlowToken"]?.ToString()
                };

                var url = jobject["Credentials"]?["CertAuthParams"]?["CertAuthUrl"]?.ToString();

                if (string.IsNullOrEmpty(url))
                {
                    throw new Exception("Certificate authentication URL not found.");
                }
                return (new Uri(url), updatedAuthFlowContext);
            }
        }

        private async Task<AuthFlowContext> GetLoginContextAsync(string loginUrl)
        {
            var response = await InternalHttpClient.GetAsync(new Uri(loginUrl)).ConfigureAwait(false);
            var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var authConfig = ParseAuthResponse(responseContent);
            if (authConfig.ContainsKey("strMainMessage"))
            {
                throw new ArgumentException(authConfig["strServiceExceptionMessage"]);
            }

            var result = new AuthFlowContext
            {
                Ctx = authConfig["sCtx"],
                FlowToken = authConfig["sFT"]
            };
            return result;
        }

        // Returns the Authorization code from the /login endpoint.
        private async Task<string> LoginAsync(
            string authority,
            string certificateToken,
            AuthFlowContext authFlowContext)
        {
            using (var loginRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{authority}/login"))
            {
                loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ctx", authFlowContext.Ctx },
                { "flowtoken", authFlowContext.FlowToken },
                { "certificatetoken", certificateToken },
            });
                var response = await InternalHttpClient.SendAsync(loginRequest).ConfigureAwait(false);
                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.Redirect)
                {
                    var loginLocation = response.Headers.Location;
                    var authCode = loginLocation.Fragment.Substring(1).Split('&')[0].Split('=')[1];
                    return authCode;
                }

                var loginResponse = ParseAuthResponse(responseContent);
                if (loginResponse.ContainsKey("strMainMessage"))
                {
                    throw new ArgumentException(loginResponse["strServiceExceptionMessage"]);
                }
                else
                {
                    throw new Exception("Login failed.");
                }
            }
        }

        private static Dictionary<string, string> ParseAuthResponse(string authResponse)
        {
            var authResponseDictionary = new Dictionary<string, string>();
            var startDelimiter = "$Config=";
            var endDelimiter = ";\n";
            var startIndex = authResponse.IndexOf(startDelimiter, StringComparison.OrdinalIgnoreCase) + startDelimiter.Length;
            var endIndex = authResponse.IndexOf(endDelimiter, startIndex, StringComparison.OrdinalIgnoreCase);
            var jsonContent = authResponse.Substring(startIndex, endIndex - startIndex);

            using (var reader = new JsonTextReader(new StringReader(jsonContent)))
            {
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.PropertyName)
                    {
                        var propertyName = reader.Value.ToString();
                        reader.Read();
                        if (reader.TokenType == JsonToken.Boolean ||
                            reader.TokenType == JsonToken.String ||
                            reader.TokenType == JsonToken.Null ||
                            reader.TokenType == JsonToken.Integer ||
                            reader.TokenType == JsonToken.Float)
                        {
                            var propertyValue = reader.Value?.ToString();
                            authResponseDictionary.Add(propertyName, propertyValue);
                        }
                        else
                        {
                            reader.Skip();
                        }
                    }
                }

                return authResponseDictionary;
            }
        }
    }
}
