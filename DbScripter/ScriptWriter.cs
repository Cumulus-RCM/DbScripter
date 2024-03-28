using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using Microsoft.SqlServer.Management.Smo;
using System.Text.Json;

namespace DbScripter;

public class ScriptWriter {
    private record sqlJson(string[] Tables, string[] Functions, string[] StoredProcedures, string[] Sequences);
    public static async Task GetScriptAsync(string connString, bool isLocal, string databaseName, Stream stream) {
       
        var sqlStrings = generateScriptsToStrings(databaseName, isLocal, connString);
        await writeJsonAsync(sqlStrings, stream).ConfigureAwait(false);

        //-----------------------------------------------------------------------------------
        static sqlJson generateScriptsToStrings(string databaseName, bool isLocal, string connString) {
            var scriptingOptions = new ScriptingOptions {
                AllowSystemObjects = false,
                ScriptOwner = false,
                NoCollation = true,
                IncludeIfNotExists = false,
                ScriptDrops = false,
                WithDependencies = true,
                Indexes = true,
                Triggers = true,
                DriForeignKeys = true,
                DriAllConstraints = true
            };
            var server = createServer(isLocal, connString);
            var header = $"--Server Version:{server.Information.Version} Generated:{DateTime.Now}";
            Console.WriteLine(header);

            var database = server.Databases[databaseName];
            var scripter =  new Scripter(server) { Options = scriptingOptions };

            var tables = database.Tables.Cast<Table>()
                .Where(tb => !tb.IsSystemObject && tb.Name != "AppliedMigrationScript")
                .Select(t => t.Urn)
                .ToArray();
            var tableSql = getSqlForDbObjects(tables, scripter);

            var functions = database.UserDefinedFunctions.Cast<UserDefinedFunction>()
                .Where(x => !x.IsSystemObject)
                .Select(t => t.Urn)
                .ToArray();
            scripter.Options.WithDependencies = false;
            var functionSql = getSqlForDbObjects(functions, scripter);

            var procedures = database.StoredProcedures.Cast<StoredProcedure>()
                .Where(x => !x.IsSystemObject)
                .Select(x => x.Urn)
                .ToArray();
            var storedProcedureSql = getSqlForDbObjects(procedures, scripter);

            var sequences = database.Sequences.Cast<Sequence>()
                .Select(x => x.Urn)
                .ToArray();
            var sequenceSql = getSqlForDbObjects(sequences, scripter);

            return new sqlJson(tableSql, functionSql, storedProcedureSql, sequenceSql);
        }

        static Server createServer(bool isLocal, string connString) {
            if (isLocal) return new Server(connString);

            using var conn = new SqlConnection(connString);
            conn.Open();
            return new Server(new ServerConnection(conn));
        }

        static string[] getSqlForDbObjects(Urn[] urns, Scripter scripter) {
            var script = scripter.Script(urns);
            var result = new List<string>(script.Count);
            foreach (var line in script) {
                if (line is null || line.StartsWith("SET")) continue;
                result.Add(line);
            }
            return result.ToArray();
        }

        static async Task writeJsonAsync(sqlJson sql, Stream stream) {
            await using var streamWriter = new StreamWriter(stream);
            var json = JsonSerializer.Serialize(sql, new JsonSerializerOptions {WriteIndented = true});
            await streamWriter.WriteAsync(json).ConfigureAwait(false);
        }
    }

    
    public static async Task CreateFormattedSqlFileAsync(string filename) {
        sqlJson? scripts;
        await using (var stream = File.Open(filename, FileMode.Open)) {
            var json = await JsonDocument.ParseAsync(stream);
            scripts = json.Deserialize<sqlJson>(new JsonSerializerOptions { WriteIndented = true });
        }
        await using var prettyStream = File.CreateText(filename + ".pretty");
        foreach(var script in  scripts!.Tables) await prettyStream.WriteAsync(script);
        foreach(var script in  scripts.Functions) await prettyStream.WriteAsync(script);
        foreach(var script in  scripts.StoredProcedures) await prettyStream.WriteAsync(script);
        foreach(var script in  scripts.Sequences) await prettyStream.WriteAsync(script);
    }
}