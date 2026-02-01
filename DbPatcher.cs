using System;
using Microsoft.Data.Sqlite;

namespace MagicMail
{
    class DbPatcher
    {
        public static void Patch()
        {
            var connectionString = "Data Source=magicmail.db";
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                
                // SQLite doesn't support IF NOT EXISTS for columns directly, so we try and catch
                command.CommandText = "ALTER TABLE EmailAliases ADD COLUMN IncludeForwardHeader INTEGER NOT NULL DEFAULT 1;";
                try
                {
                    command.ExecuteNonQuery();
                    // Console.WriteLine("Auto-Migration: Added IncludeForwardHeader column.");
                }
                catch (SqliteException ex) when (ex.Message.Contains("duplicate column name"))
                {
                    // Column already exists, ignore
                }
                catch (Exception)
                {
                    // Ignore other errors to not block startup
                }
            }
        }
    }
}
