﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class PriorityOrderer : ITestCaseOrderer
    {
        public PriorityOrderer(IMessageSink messageSink)
        {
        }

        public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
            where TTestCase : ITestCase
        {
            var sortedMethods = new SortedDictionary<int, List<TTestCase>>();

            foreach (TTestCase testCase in testCases)
            {
                var priority = 0;

                foreach (IAttributeInfo attr in testCase.TestMethod.Method.GetCustomAttributes(typeof(TestPriorityAttribute).AssemblyQualifiedName))
                {
                    priority = attr.GetNamedArgument<int>("Priority");
                }

                GetOrCreate(sortedMethods, priority).Add(testCase);
            }

            foreach (var list in sortedMethods.Keys.Select(priority => sortedMethods[priority]))
            {
                list.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.TestMethod.Method.Name, y.TestMethod.Method.Name));

                foreach (TTestCase testCase in list)
                {
                    yield return testCase;
                }
            }
        }

        public static TValue GetOrCreate<TKey, TValue>(IDictionary<TKey, TValue> dictionary, TKey key)
            where TValue : new()
        {
            if (dictionary.TryGetValue(key, out TValue result))
            {
                return result;
            }

            result = new TValue();
            dictionary[key] = result;

            return result;
        }
    }
}
