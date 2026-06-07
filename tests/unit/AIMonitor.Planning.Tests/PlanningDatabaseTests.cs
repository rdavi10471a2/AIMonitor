using AIMonitor.Core;
using Microsoft.Data.Sqlite;

namespace AIMonitor.Planning.Tests
{
    public sealed class PlanningDatabaseTests
    {
        [Fact]
        public void EnsureCreated_initializes_schema_idempotently()
        {
            using (PlanningTestWorkspace workspace = PlanningTestWorkspace.Create())
            {
                PlanningDatabase database = new PlanningDatabase(workspace.Settings);

                database.EnsureCreated();
                database.EnsureCreated();

                using (SqliteConnection connection = database.OpenConnection())
                {
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "select count(*) from board_state;";
                        long count = (long)(command.ExecuteScalar() ?? 0L);

                        Assert.Equal(1L, count);
                    }
                }
            }
        }
    }
}
