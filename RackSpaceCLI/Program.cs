using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using nietras.SeparatedValues;
using Formatting = Newtonsoft.Json.Formatting;

namespace RackSpaceCLI;

// ** These are the rate limits RackSpace imposes on clients **
// Operation Category	Request Limit
//     GET	                                    60 per minute
//     PUT, POST, DELETE (mailboxes)	        30 per minute
//     POST, PUT, DELETE on a domain	        2 per minute
//     POST, DELETE on alternate domains	    2 per minute
// Enabling public folders for a domain	1 per   5 minutes

// Therefore, to mitigate 403 codes returning from the API, a wait is placed on GET and DELETE operations and a
// query limit of 30 is places on GET requests

// Delete group of mailbox
// Delete a single domain
// Delete group of domains from csv

public class Program
{
    private static readonly List<string> _domains = [];

    public static void Main(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args)
            .WithParsed(opt => RunWithOptions(opt))
            .WithNotParsed(HandleParseError);
    }


    private static void RunWithOptions(Options options)
    {
        if (!string.IsNullOrEmpty(options.FilePath))
        {
            Console.WriteLine(options.FilePath);
            LookUpCustomerByDomain(options.FilePath);
        }
        else if (!string.IsNullOrEmpty(options.Domain) && IsValidDomain(options.Domain))
        {
            Console.WriteLine(options.Domain);
        }
        else if (!string.IsNullOrEmpty(options.Mailbox) && IsValidEmail(options.Mailbox))
        {
            Console.WriteLine(options.Mailbox);
        }
        else
        {
            Console.WriteLine("No valid options were provided");
            // Display help flags
        }
    }

    private static void HandleParseError(IEnumerable<Error> errors)
    {
        Console.WriteLine($"ERRORS: {errors}");
    }

    private static void LookUpCustomerByDomain(string path)
    {
        ReadCsvFile(path);
        try
        {
            const int batchSize = 59;
            const int delayTime = 60_000; // 60 seconds

            for (var i = 0; i < _domains.Count; i += batchSize)
            {
                var currentBatch = _domains.Skip(i).Take(batchSize);

                foreach (var domain in currentBatch)
                {
                    var response = Client(domain, Method.GET);
                    using var streamReader = new StreamReader(response.GetResponseStream());
                    var content = streamReader.ReadToEnd();
                    Console.WriteLine(JToken.Parse(content).ToString(Formatting.Indented));
                }

                if (i + batchSize >= _domains.Count) continue;
                Console.WriteLine("Pausing for 1 minute to satisfy rate limit...\r\n");
                Thread.Sleep(delayTime);
            }

            ListDomains();
        }
        catch (Exception ex)
        {
            throw new Exception("An error has occurred: " + ex.Message);
        }
    }


    private static async Task RemoveDomain()
    {
        try
        {
            ReadCsv();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }

        try
        {
            const int batchSize = 3;
            const int delayTime = 60_000; //60 seconds
            for (var i = 0; i < _domains.Count; i += batchSize)
            {
                var currentBatch = _domains.Skip(i).Take(batchSize);
                foreach (var domain in currentBatch)
                {
                    var response = Client(domain, Method.DELETE);
                    using var reader = new StreamReader(response.GetResponseStream());
                    var content = await reader.ReadToEndAsync();
                    Console.WriteLine(JToken.Parse(content).ToString(Formatting.Indented));
                }

                if (i + batchSize >= _domains.Count) continue;
                Console.WriteLine("Pausing for 1 minute to satisfy rate limit...\r\n");
                await Task.Delay(delayTime);
            }

            ListDomains();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error has occured: {ex.Message}");
        }
    }

    private static void RemoveMailboxes(string domain)
    {
    }

    private static HttpWebResponse Client(string domain, Method method, string data = "")
    {
        try
        {
            var rs = new RestApiClient("https://api.emailsrvr.com/", "Db45bOTFnlsOzOoO0fbr",
                "BpVIGvEneUVBFXUBt2xyRUay5dT0iygvw6XmkXCv");

            return method switch
            {
                Method.GET => rs.Get($"customers/all/domains/{domain}",
                    "application/json"),
                Method.POST => rs.Post($"customers/all/domains/{domain}", data,
                    "application/json"),
                Method.DELETE => rs.Delete($"customers/all/domains/{domain}",
                    "application/json"),
                _ => throw new Exception("Invalid method passed")
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error has occurred: {ex.Message}");
            throw;
        }
    }

    private static string ReaderXmlContentReturnJson(StreamReader reader)
    {
        try
        {
            var content = reader.ReadToEnd();
            var xDoc = XDocument.Parse(content);
            return JsonConvert.SerializeXNode(xDoc.Root, Formatting.Indented, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }

    private static List<string> GetValidDomains(string jsonData)
    {
        var jsonObject = JObject.Parse(jsonData);

        if (jsonObject.ContainsKey("code")) return new List<string>();

        if (jsonObject.ContainsKey("aliases")) return new List<string> { jsonObject["name"].ToString() };


        return jsonObject.Children()
            .OfType<JObject>()
            .Where(domain => !domain.ContainsKey("code"))
            .Select(domain => domain["name"].ToString())
            .ToList();
    }

    private static void ListDomains()
    {
        foreach (var domain in _domains) Console.WriteLine($"Domain: {domain}");
    }


    private static void ReadCsvFile(string path)
    {
        try
        {
            using var reader = Sep.Reader().FromFile(path);
            _domains.Clear();

            foreach (var row in reader)
            {
                var domain = row[0].ToString().Split('@');
                _domains.Add(domain[1]);
            }

            Console.WriteLine($"Total domains read from file: {_domains.Count}");
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"The file \"{path}\" is not a valid file name or does not exist.\r\n" +
                                    $"Please check for typos or the correct path");
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"The file \"{path}\" is not in a valid CSV format.\r\n" +
                                    "Check for missing commas or other anomalies within the file.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }

    private static void ReadCsv()
    {
        try
        {
            using var reader = Sep.Reader()
                .FromFile("/Users/drew/RiderProjects/RackSpaceCLI/RackSpaceCLI/mail_domains.csv");
            foreach (var row in reader)
            {
                var domain = row[0].ToString().Split('@');
                // Console.WriteLine(domain[1]);
                _domains.Add(domain[1]);
            }

            Console.WriteLine(_domains.Count);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return false;

        try
        {
            email = Regex.Replace(email, @"(@)(.+)$", DomainMapper, RegexOptions.None, TimeSpan.FromMilliseconds(200));

            static string DomainMapper(Match match)
            {
                var idn = new IdnMapping();
                var domainName = idn.GetAscii(match.Groups[2].Value);

                return match.Groups[1].Value + domainName;
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            return false;
        }
        catch (ArgumentException ex)
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(email,
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException ex)
        {
            return false;
        }
    }

    private static bool IsValidDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return false;

        try
        {
            return Regex.IsMatch(domain,
                @"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,6}$",
                RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private enum Method
    {
        GET,
        POST,
        DELETE
    }
}