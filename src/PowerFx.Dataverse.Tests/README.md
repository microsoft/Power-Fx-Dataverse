Configuration and Setup
Install a version of Visual Studio that includes SQL

From Visual Studio:

- View -> SQL Server Object Explorer

- Choose an instance

- On Databases, right click and Create New Database and give it a name

- On the database, right click and choose Properties

- Set an environment variable FxTestSQLDatabase to the value of the ConnectionString

- Restart Visual Studio

NOTE:
If setting the environment variable and restarting doesn't work, a manual workaround is to set the variable in the test method itself: 
- Environment.SetEnvironmentVariable("FxTestSQLDatabase", @ConnectionString)

Make sure to remove the statement before checking in any changes to the test file. 

