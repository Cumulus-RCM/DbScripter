namespace DbScripter;

public class Program {
    static async Task Main(string[] args) {
        const string DB_NAME = "Billing"; // database name  

        const string LOCAL = "(localdb)\\MSSQLLocalDB";

        var azureConnString =
            "Server=tcp:cumulus-dev-db.database.windows.net,1433;Integrated Security=false;Authentication=Active Directory Interactive";

        const bool IS_LOCAL = false;
        var connString = IS_LOCAL ? LOCAL : azureConnString;

        var filename = @"c:\temp\db.json";
        var fileStream = new FileStream(filename, FileMode.Create);
        await ScriptWriter.GetScriptAsync(connString, IS_LOCAL, DB_NAME, fileStream);
        fileStream.Close();
        fileStream.Dispose();

        await ScriptWriter.CreateFormattedSqlFileAsync(filename);
    }
}
    