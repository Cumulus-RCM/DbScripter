using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using Microsoft.SqlServer.Management.Smo;
using System.Text.Json;

namespace DbScripter;

public class ScriptWriter {
    private record sqlJson(string ScriptValue = "");

    public static async Task GetScriptAsync(string connString, bool isLocal, string databaseName, Stream stream) {
       
        var sqlStrings = generateScriptsToStrings(databaseName, isLocal, connString);
        await writeJsonAsync(sqlStrings, stream).ConfigureAwait(false);

        //-----------------------------------------------------------------------------------
        static string[] generateScriptsToStrings(string databaseName, bool isLocal, string connString) {
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
            var generatedSql = new string[3];
            var scripter =  new Scripter(server) { Options = scriptingOptions };

            var tables = database.Tables.Cast<Table>()
                .Where(tb => !tb.IsSystemObject && tb.Name != "AppliedMigrationScript")
                .Select(t => t.Urn)
                .ToArray();
            generatedSql[0] = getSqlForDbObjects(tables, scripter);

            var functions = database.UserDefinedFunctions.Cast<UserDefinedFunction>()
                .Where(x => !x.IsSystemObject)
                .Select(t => t.Urn)
                .ToArray();
            scripter.Options.WithDependencies = false;
            generatedSql[1] = getSqlForDbObjects(functions, scripter);

            var procedures = database.StoredProcedures.Cast<StoredProcedure>()
                .Where(x => !x.IsSystemObject)
                .Select(x => x.Urn)
                .ToArray();
            generatedSql[2] = getSqlForDbObjects(procedures, scripter);

            return generatedSql;
        }

        static Server createServer(bool isLocal, string connString) {
            if (isLocal) return new Server(connString);

            using var conn = new SqlConnection(connString);
            conn.Open();
            return new Server(new ServerConnection(conn));
        }

        static string getSqlForDbObjects(Urn[] urns, Scripter scripter) {
            var script = scripter.Script(urns);
            var sb = new StringBuilder();
            foreach (var line in script) {
                if (line is null || line.StartsWith("SET")) continue;
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        static async Task writeJsonAsync(IEnumerable<string> sqlStrings, Stream stream) {
            var json = JsonSerializer.Serialize(sqlStrings.Select(s => new sqlJson(s)));
            await using var streamWriter = new StreamWriter(stream);
            await streamWriter.WriteAsync(json).ConfigureAwait(false);
        }
    }

    
    public static async Task CreatePrettyFileAsync(string filename) {
        var stream = File.Open(filename, FileMode.Open);
        var json = await JsonDocument.ParseAsync(stream);
        var pretty = json.Deserialize<IEnumerable<sqlJson>>(new JsonSerializerOptions { WriteIndented = true });
        await using var prettyStream = File.CreateText(filename + ".pretty");
        foreach (var s in pretty) {
            await prettyStream.WriteAsync(s.ScriptValue);
        }
    }
}