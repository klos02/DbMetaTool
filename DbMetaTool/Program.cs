using System;
using System.IO;
using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool
{
    public static class Program
    {
        // Przykładowe wywołania:
        // DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
        // DbMetaTool export-scripts --connection-string "..." --output-dir "C:\out"
        // DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Użycie:");
                Console.WriteLine("  build-db --db-dir <ścieżka> --scripts-dir <ścieżka>");
                Console.WriteLine("  export-scripts --connection-string <connStr> --output-dir <ścieżka>");
                Console.WriteLine("  update-db --connection-string <connStr> --scripts-dir <ścieżka>");
                return 1;
            }

            try
            {
                var command = args[0].ToLowerInvariant();

                switch (command)
                {
                    case "build-db":
                        {
                            string dbDir = GetArgValue(args, "--db-dir");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            BuildDatabase(dbDir, scriptsDir);
                            Console.WriteLine("Baza danych została zbudowana pomyślnie.");
                            return 0;
                        }

                    case "export-scripts":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string outputDir = GetArgValue(args, "--output-dir");

                            ExportScripts(connStr, outputDir);
                            Console.WriteLine("Skrypty zostały wyeksportowane pomyślnie.");
                            return 0;
                        }

                    case "update-db":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            UpdateDatabase(connStr, scriptsDir);
                            Console.WriteLine("Baza danych została zaktualizowana pomyślnie.");
                            return 0;
                        }

                    default:
                        Console.WriteLine($"Nieznane polecenie: {command}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Błąd: " + ex.Message);
                return -1;
            }
        }

        private static string GetArgValue(string[] args, string name)
        {
            int idx = Array.IndexOf(args, name);
            if (idx == -1 || idx + 1 >= args.Length)
                throw new ArgumentException($"Brak wymaganego parametru {name}");
            return args[idx + 1];
        }

        /// <summary>
        /// Buduje nową bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void BuildDatabase(string databaseDirectory, string scriptsDirectory)
        {
            // 1) Utwórz pustą bazę danych FB 5.0 w katalogu databaseDirectory.
            // Auto-detekcja: "/" na początku = serwer Linux (Docker), inaczej = embedded lokalnie
            bool isServerPath = databaseDirectory.StartsWith("/");

            string dbPath;
            var csb = new FbConnectionStringBuilder
            {
                UserID = "SYSDBA",
                Password = "masterkey",
                Charset = "UTF8"
            };

            if (isServerPath)
            {
                // Ścieżka na serwerze zdalnym (np. Docker)
                dbPath = databaseDirectory.EndsWith(".fdb")
                    ? databaseDirectory
                    : databaseDirectory.TrimEnd('/') + "/database.fdb";
                csb.DataSource = "localhost";
                csb.Port = 3050;
                csb.ServerType = FbServerType.Default;
            }
            else
            {
                // Ścieżka lokalna (embedded)
                if (!Directory.Exists(databaseDirectory))
                    Directory.CreateDirectory(databaseDirectory);
                dbPath = Path.Combine(databaseDirectory, "database.fdb");
                csb.ServerType = FbServerType.Embedded;
            }

            csb.Database = dbPath;

            FbConnection.CreateDatabase(csb.ConnectionString, pageSize: 16384, forcedWrites: true, overwrite: true);
            Console.WriteLine($"Utworzono bazę danych: {dbPath}");

            // 2) Wczytaj i wykonaj kolejno skrypty z katalogu scriptsDirectory
            if (!Directory.Exists(scriptsDirectory))
                throw new DirectoryNotFoundException($"Katalog skryptów nie istnieje: {scriptsDirectory}");

            var scriptFiles = Directory.GetFiles(scriptsDirectory, "*.sql", SearchOption.AllDirectories);
            Array.Sort(scriptFiles);

            int successCount = 0;
            int errorCount = 0;

            using (var connection = new FbConnection(csb.ConnectionString))
            {
                connection.Open();

                foreach (var scriptFile in scriptFiles)
                {
                    try
                    {
                        string script = File.ReadAllText(scriptFile);

                        using (var command = new FbCommand(script, connection))
                        {
                            command.ExecuteNonQuery();
                        }

                        Console.WriteLine($"  [OK] {Path.GetFileName(scriptFile)}");
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  [BŁĄD] {Path.GetFileName(scriptFile)}: {ex.Message}");
                        errorCount++;
                    }
                }
            }

            // 3) Obsłuż błędy i wyświetl raport.
            Console.WriteLine();
            Console.WriteLine($"Raport: {successCount} skryptów wykonanych, {errorCount} błędów.");

            if (errorCount > 0)
                throw new Exception($"Wystąpiły błędy podczas wykonywania {errorCount} skryptów.");
        }

        /// <summary>
        /// Generuje skrypty metadanych z istniejącej bazy danych Firebird 5.0.
        /// </summary>
        public static void ExportScripts(string connectionString, string outputDirectory)
        {
            // 1) Połącz się z bazą danych przy użyciu connectionString.
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            using (var connection = new FbConnection(connectionString))
            {
                connection.Open();

                // 2) Pobierz metadane domen, tabel (z kolumnami) i procedur.

                // Eksport domen
                string domainsPath = Path.Combine(outputDirectory, "01_domains.sql");
                using (var writer = new StreamWriter(domainsPath))
                {
                    writer.WriteLine("-- Domeny (DOMAINS)");
                    writer.WriteLine();

                    using (var cmd = new FbCommand(@"
                        SELECT f.RDB$FIELD_NAME, f.RDB$FIELD_TYPE, f.RDB$FIELD_LENGTH,
                               f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, f.RDB$NULL_FLAG,
                               f.RDB$DEFAULT_SOURCE, f.RDB$VALIDATION_SOURCE, f.RDB$CHARACTER_LENGTH
                        FROM RDB$FIELDS f
                        WHERE f.RDB$FIELD_NAME NOT STARTING WITH 'RDB$'
                          AND f.RDB$FIELD_NAME NOT STARTING WITH 'SEC$'
                          AND f.RDB$FIELD_NAME NOT STARTING WITH 'MON$'
                        ORDER BY f.RDB$FIELD_NAME", connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string name = reader.GetString(0).Trim();
                            int fieldType = reader.GetInt16(1);
                            string sqlType = MapFieldType(fieldType, reader);
                            bool notNull = !reader.IsDBNull(5) && reader.GetInt16(5) == 1;

                            writer.WriteLine($"CREATE DOMAIN {name} AS {sqlType}{(notNull ? " NOT NULL" : "")};");
                        }
                    }
                }
                Console.WriteLine($"  Wyeksportowano domeny: {domainsPath}");

                // Eksport tabel
                string tablesPath = Path.Combine(outputDirectory, "02_tables.sql");
                using (var writer = new StreamWriter(tablesPath))
                {
                    writer.WriteLine("-- Tabele (TABLES)");
                    writer.WriteLine();

                    using (var cmdTables = new FbCommand(@"
                        SELECT r.RDB$RELATION_NAME
                        FROM RDB$RELATIONS r
                        WHERE r.RDB$SYSTEM_FLAG = 0
                          AND r.RDB$VIEW_BLR IS NULL
                        ORDER BY r.RDB$RELATION_NAME", connection))
                    using (var readerTables = cmdTables.ExecuteReader())
                    {
                        var tables = new List<string>();
                        while (readerTables.Read())
                            tables.Add(readerTables.GetString(0).Trim());

                        foreach (var table in tables)
                        {
                            writer.WriteLine($"CREATE TABLE {table} (");

                            using (var cmdCols = new FbCommand(@"
                                SELECT rf.RDB$FIELD_NAME, rf.RDB$FIELD_SOURCE,
                                       f.RDB$FIELD_TYPE, f.RDB$FIELD_LENGTH,
                                       f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE,
                                       rf.RDB$NULL_FLAG, rf.RDB$DEFAULT_SOURCE, f.RDB$CHARACTER_LENGTH
                                FROM RDB$RELATION_FIELDS rf
                                JOIN RDB$FIELDS f ON rf.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME
                                WHERE rf.RDB$RELATION_NAME = @tableName
                                ORDER BY rf.RDB$FIELD_POSITION", connection))
                            {
                                cmdCols.Parameters.AddWithValue("@tableName", table);
                                using (var readerCols = cmdCols.ExecuteReader())
                                {
                                    var columns = new List<string>();
                                    while (readerCols.Read())
                                    {
                                        string colName = readerCols.GetString(0).Trim();
                                        string fieldSource = readerCols.GetString(1).Trim();
                                        bool notNull = !readerCols.IsDBNull(6) && readerCols.GetInt16(6) == 1;

                                        string colType;
                                        if (!fieldSource.StartsWith("RDB$"))
                                            colType = fieldSource; // Domain
                                        else
                                            colType = MapFieldType(readerCols.GetInt16(2), readerCols);

                                        columns.Add($"    {colName} {colType}{(notNull ? " NOT NULL" : "")}");
                                    }
                                    writer.WriteLine(string.Join(",\n", columns));
                                }
                            }

                            writer.WriteLine(");");
                            writer.WriteLine();
                        }
                    }
                }
                Console.WriteLine($"  Wyeksportowano tabele: {tablesPath}");

                // Eksport procedur
                string procsPath = Path.Combine(outputDirectory, "03_procedures.sql");
                using (var writer = new StreamWriter(procsPath))
                {
                    writer.WriteLine("-- Procedury (PROCEDURES)");
                    writer.WriteLine();

                    using (var cmdProcs = new FbCommand(@"
                        SELECT p.RDB$PROCEDURE_NAME, p.RDB$PROCEDURE_SOURCE
                        FROM RDB$PROCEDURES p
                        WHERE p.RDB$SYSTEM_FLAG = 0
                        ORDER BY p.RDB$PROCEDURE_NAME", connection))
                    using (var readerProcs = cmdProcs.ExecuteReader())
                    {
                        var procedures = new List<(string Name, string? Source)>();
                        while (readerProcs.Read())
                        {
                            procedures.Add((
                                readerProcs.GetString(0).Trim(),
                                readerProcs.IsDBNull(1) ? null : readerProcs.GetString(1)
                            ));
                        }

                        foreach (var (procName, procSource) in procedures)
                        {
                            writer.WriteLine($"-- Procedura: {procName}");

                            // Pobierz parametry wejściowe
                            var inputParams = new List<string>();
                            var outputParams = new List<string>();

                            using (var cmdParams = new FbCommand(@"
                                SELECT pp.RDB$PARAMETER_NAME, pp.RDB$PARAMETER_TYPE,
                                       f.RDB$FIELD_TYPE, f.RDB$FIELD_LENGTH,
                                       f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE,
                                       pp.RDB$FIELD_SOURCE, f.RDB$CHARACTER_LENGTH
                                FROM RDB$PROCEDURE_PARAMETERS pp
                                JOIN RDB$FIELDS f ON pp.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME
                                WHERE pp.RDB$PROCEDURE_NAME = @procName
                                ORDER BY pp.RDB$PARAMETER_TYPE, pp.RDB$PARAMETER_NUMBER", connection))
                            {
                                cmdParams.Parameters.AddWithValue("@procName", procName);
                                using (var readerParams = cmdParams.ExecuteReader())
                                {
                                    while (readerParams.Read())
                                    {
                                        string paramName = readerParams.GetString(0).Trim();
                                        int paramType = readerParams.GetInt16(1);
                                        string fieldSource = readerParams.GetString(6).Trim();

                                        string paramTypeName;
                                        if (!fieldSource.StartsWith("RDB$"))
                                            paramTypeName = fieldSource;
                                        else
                                            paramTypeName = MapFieldTypeForParam(readerParams);

                                        if (paramType == 0)
                                            inputParams.Add($"{paramName} {paramTypeName}");
                                        else
                                            outputParams.Add($"{paramName} {paramTypeName}");
                                    }
                                }
                            }

                            writer.Write($"CREATE OR ALTER PROCEDURE {procName}");
                            if (inputParams.Count > 0)
                                writer.Write($" ({string.Join(", ", inputParams)})");
                            writer.WriteLine();

                            if (outputParams.Count > 0)
                                writer.WriteLine($"RETURNS ({string.Join(", ", outputParams)})");

                            writer.WriteLine("AS");
                            if (!string.IsNullOrEmpty(procSource))
                                writer.WriteLine(procSource.Trim());
                            writer.WriteLine("^");
                            writer.WriteLine();
                        }
                    }
                }
                Console.WriteLine($"  Wyeksportowano procedury: {procsPath}");
            }

            // 3) Wygeneruj pliki .sql / .json / .txt w outputDirectory.
            Console.WriteLine();
            Console.WriteLine($"Eksport zakończony do katalogu: {outputDirectory}");
        }

        private static string MapFieldType(int fieldType, FbDataReader reader)
        {
            int precision = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader[4]);
            int scale = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader[5]);
            int charLength = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader[8]);

            return MapFieldTypeCore(fieldType, precision, scale, charLength);
        }

        private static string MapFieldTypeForParam(FbDataReader reader)
        {
            int fieldType = reader.GetInt16(2);
            int precision = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader[4]);
            int scale = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader[5]);
            int charLength = reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader[7]);

            return MapFieldTypeCore(fieldType, precision, scale, charLength);
        }

        private static string MapFieldTypeCore(int fieldType, int precision, int scale, int charLength)
        {
            return fieldType switch
            {
                7 => scale < 0 ? $"NUMERIC({precision}, {-scale})" : "SMALLINT",
                8 => scale < 0 ? $"NUMERIC({precision}, {-scale})" : "INTEGER",
                10 => "FLOAT",
                12 => "DATE",
                13 => "TIME",
                14 => $"CHAR({charLength})",
                16 => scale < 0 ? $"NUMERIC({precision}, {-scale})" : "BIGINT",
                27 => "DOUBLE PRECISION",
                35 => "TIMESTAMP",
                37 => $"VARCHAR({charLength})",
                261 => "BLOB",
                _ => $"UNKNOWN({fieldType})"
            };
        }

        /// <summary>
        /// Aktualizuje istniejącą bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void UpdateDatabase(string connectionString, string scriptsDirectory)
        {
            // 1) Połącz się z bazą danych przy użyciu connectionString.
            if (!Directory.Exists(scriptsDirectory))
                throw new DirectoryNotFoundException($"Katalog skryptów nie istnieje: {scriptsDirectory}");

            var scriptFiles = Directory.GetFiles(scriptsDirectory, "*.sql", SearchOption.AllDirectories);
            Array.Sort(scriptFiles);

            if (scriptFiles.Length == 0)
            {
                Console.WriteLine("Brak skryptów do wykonania.");
                return;
            }

            int successCount = 0;
            int errorCount = 0;

            // 2) Wykonaj skrypty z katalogu scriptsDirectory (tylko obsługiwane elementy).
            using (var connection = new FbConnection(connectionString))
            {
                connection.Open();

                // 3) Zadbaj o poprawną kolejność i bezpieczeństwo zmian.
                foreach (var scriptFile in scriptFiles)
                {
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            string script = File.ReadAllText(scriptFile);

                            using (var command = new FbCommand(script, connection, transaction))
                            {
                                command.ExecuteNonQuery();
                            }

                            transaction.Commit();
                            Console.WriteLine($"  [OK] {Path.GetFileName(scriptFile)}");
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            Console.WriteLine($"  [BŁĄD] {Path.GetFileName(scriptFile)}: {ex.Message}");
                            errorCount++;
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Raport: {successCount} skryptów wykonanych, {errorCount} błędów.");

            if (errorCount > 0)
                throw new Exception($"Wystąpiły błędy podczas wykonywania {errorCount} skryptów.");
        }
    }
}
