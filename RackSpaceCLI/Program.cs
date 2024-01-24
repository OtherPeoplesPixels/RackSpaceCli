using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using CommandLine;
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
    private static readonly List<string> _validDomains = [];
    private static readonly List<string> _invalidDomains = [];
    private static readonly List<string> _domainsWithoutMailbox = [];
    private static List<string> _mailBoxes = [];

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
                    ProcessDomains(content, domain);
                    Console.WriteLine(JToken.Parse(content).ToString(Formatting.Indented));
                }

                if (i + batchSize >= _domains.Count) continue;
                Console.WriteLine("Pausing for 1 minute to satisfy rate limit...\r\n");
                Thread.Sleep(delayTime);
            }
            SyncMailboxesWithValidDomains();
            if (_mailBoxes.Count == 0)
            {
                DomainDeletePrompt();
                return;
            }
            
            
            DumpInvalidDomainsToCSV("Csv_files/Domains_without_email.csv", "Csv_files/Domains_not_found.csv");
            MailboxDeletePrompt();
            
        }
        catch (Exception ex)
        {
            throw new Exception("An error has occurred: " + ex.Message);
        }
    }

    

    
    private static void RemoveDomain()
    {

        try
        {
            const int batchSize = 3;
            const int delayTime = 60_000; //60 seconds
            for (var i = 0; i < _domainsWithoutMailbox.Count; i += batchSize)
            {
                var currentBatch = _domains.Skip(i).Take(batchSize);
                foreach (var domain in currentBatch)
                {
                    var response = Client(domain, Method.DELETE);
                    using var reader = new StreamReader(response.GetResponseStream());
                    var content =  reader.ReadToEnd();
                    ProcessDomainResponse(content, domain);
                    
                }

                if (i + batchSize >= _domains.Count) continue;
                Console.WriteLine("Pausing for 1 minute to satisfy rate limit...\r\n");
                Thread.Sleep(delayTime);
            }

            Console.WriteLine($"All domains deleted.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error has occurred: {ex.Message}");
        }
    }

    private static void ProcessDomainResponse(string content, string domain)
    {
        try
        {
            var data = JObject.Parse(content);

            if (data.ContainsKey("itemNotFoundFault"))
            {
                Console.WriteLine($"{domain} not found.");
            }
            else if(content == "" || string.IsNullOrEmpty(content))
            {
                Console.WriteLine($"{domain} was successfully deleted.");
            }
        }
        catch (Exception ex)
        {
            if (string.IsNullOrEmpty(content))
            {
                Console.WriteLine($"Domain {domain} was successfully deleted.");
            }
            else
            {
                Console.WriteLine("Mailbox deletion failed: Error parsing JSON response. Details: " + ex.Message);
            }
        }
    }

    private static void RemoveMailboxes()
    {
       
        try
        {
            var rs = new RestApiClient("https://api.emailsrvr.com/", "Db45bOTFnlsOzOoO0fbr",
                "BpVIGvEneUVBFXUBt2xyRUay5dT0iygvw6XmkXCv");
            const int batchSize = 29;
            const int delayTime = 60_000; //60 seconds
            
            for (var i = 0; i < _validDomains.Count; i += batchSize)
            {
                var currentBatch = _mailBoxes.Skip(i).Take(batchSize);
                foreach (var domain in currentBatch)
                {
                    var response = rs.Delete($"customers/all/domains/{domain.Split("@").Last()}/rs/mailboxes/{domain.Split("@").First()}",
                        "application/json");
                    using var reader = new StreamReader(response.GetResponseStream());
                    var content =  reader.ReadToEnd();

                    ParseMailResponse(content, domain);
                }

                if (i + batchSize >= _domains.Count) continue;
                Console.WriteLine("Pausing for 1 minute to satisfy rate limit...\r\n");
                Thread.Sleep(delayTime);
            }
            
            DomainDeletePrompt();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private static void ParseMailResponse(string content, string domain)
    {
        try
        {
            var data = JObject.Parse(content);

            if (data.ContainsKey("itemNotFoundFault"))
            {
                Console.WriteLine("Mailbox not found.");
            }
            else
            {
                Console.WriteLine($"Mailbox {domain} was successfully deleted.");
            }
        }
        catch (Exception ex)
        {
            if (string.IsNullOrEmpty(content))
            {
                Console.WriteLine($"Mailbox {domain} was successfully deleted.");
            }
            else
            {
                Console.WriteLine("Mailbox deletion failed: Error parsing JSON response. Details: " + ex.Message);
            }
        }
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
    

    private static void ProcessDomains(string json, string domain)
    {
        var jsonObject = JObject.Parse(json);

        if (jsonObject["unauthorizedFault"]?["code"] != null)
        {
            _invalidDomains.Add(domain);
        }
        else if (jsonObject["itemNotFoundFault"]?["code"] != null)
        {
            _invalidDomains.Add(domain);
        }
        else if (jsonObject["rsEmailUsedStorage"] != null && (int)jsonObject["rsEmailUsedStorage"] == 0)
        {
            Console.WriteLine($"Domain has no email: {domain}");
            _domainsWithoutMailbox.Add(domain);
            
        }
        else if (jsonObject["name"] != null && (int)jsonObject["rsEmailUsedStorage"] == 1)
        {
            _validDomains.Add(jsonObject["name"].ToString());
            
        }
    }

    private static void SyncMailboxesWithValidDomains()
    {
        var accountDict = new Dictionary<string, string>();
        _mailBoxes = _mailBoxes.Where(mb => _validDomains.Contains(mb.Split("@").Last())).ToList();

        foreach (var mailBox in _mailBoxes)
        {
            Console.WriteLine(mailBox);
            accountDict.Add(mailBox, mailBox.Split('@').Last());
            
        }

        Console.WriteLine($"There are {_mailBoxes.Count} mailboxes to process.");
    }
    

    private static void ListDomains()
    {
        foreach (var domain in _validDomains) Console.WriteLine($"Domain: {domain}");
        Console.WriteLine($"Total valid domains: {_validDomains.Count}");
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
                var mailbox = (row[0].ToString());
                _domains.Add(domain[1]);
                _mailBoxes.Add(mailbox);
            }

            foreach (var mailBox in _mailBoxes)
            {
                Console.WriteLine(mailBox);
            }
            Console.WriteLine($"Total domains read from file: {_domains.Count}");
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"The file \"{path}\" is not a valid file name or does not exist in the location specified.\r\n" +
                                    $"Please check for typos in the filename or the correct path");
            Environment.Exit(0);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"The file \"{path}\" is not in a valid CSV format.\r\n" +
                                    "Check for missing commas or other anomalies within the file.");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unexpected error has occurred, please contact the development team and provide them with the following information:\r\n"
                + $"Error: {ex.Message}");
            
            Environment.Exit(0);
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

    private static void MailboxDeletePrompt()
    {
        Console.WriteLine("Do you want to delete the mailboxes associated with the listed domains? (y/n/a)");

        while (true)
        {
            Console.Write("Enter your choice (y = yes, n = no, a = abort): ");
            string userInput = Console.ReadLine().Trim().ToLower();

            if (userInput == "y")
            {
                Console.WriteLine("Running the delete function...");
                RemoveMailboxes();
                break;
            }
            else if (userInput == "n")
            {
                Console.WriteLine("Operation cancelled.");
                break;
            }
            else if (userInput == "a")
            {
                Console.WriteLine("Operation aborted.");
                Environment.Exit(0); 
            }
            else
            {
                Console.WriteLine("Invalid input. Please enter 'y', 'n', or 'a'.");
            }
        }
    }

    private static void DomainDeletePrompt()
    {
        Console.WriteLine("Do you want to delete the domains that no longer have mailboxes? (y/n/a)");

        while (true)
        {
            Console.Write("Enter your choice (y = yes, n = no, a = abort): ");
            string userInput = Console.ReadLine().Trim().ToLower();

            if (userInput == "y")
            {
                Console.WriteLine("Running the delete function...");
                RemoveDomain();
                break;
            }
            else if (userInput == "n")
            {
                Console.WriteLine("Operation cancelled.");
                break;
            }
            else if (userInput == "a")
            {
                Console.WriteLine("Operation aborted.");
                Environment.Exit(0); 
            }
            else
            {
                Console.WriteLine("Invalid input. Please enter 'y', 'n', or 'a'.");
            }
        }
    }
    
    
    private static void DumpInvalidDomainsToCSV(string pathToNoEmailFile, string pathToNoDomainFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(pathToNoEmailFile));
        Directory.CreateDirectory(Path.GetDirectoryName(pathToNoDomainFile));
        using (var writer = new StreamWriter(pathToNoEmailFile, true))
        {
            
            if (!File.Exists(pathToNoEmailFile))
            {
                writer.WriteLine("Domain,Value1,Value2"); 
            }
            
            foreach (var domain in _domainsWithoutMailbox.Distinct())
            {
                if (!File.ReadLines(pathToNoEmailFile).Any(line => line.StartsWith(domain)))
                {
                    writer.WriteLine($"{domain},{DateTime.Now:MM/dd/yyyy},x"); 
                }
            }
        }

        using (var writer = new StreamWriter(pathToNoDomainFile, true))
        {
            if (!File.Exists(pathToNoDomainFile))
            {
                writer.WriteLine("Domain,Value1,Value2"); 
            }

            foreach (var domain in _invalidDomains)
            {
                if (!File.ReadLines(pathToNoDomainFile).Any(line => line.StartsWith(domain)))
                {
                    writer.WriteLine($"{domain},{DateTime.Now:MM/dd/yyyy},x");
                }
            }
        }

        var absolutePath = Path.GetFullPath(pathToNoEmailFile);
        Console.WriteLine($"A Csv file has been created or updated at {absolutePath}");

        foreach (var mailbox in _domainsWithoutMailbox)
        {
            Console.WriteLine($" no mail box: {mailbox}");
        }

        foreach (var domain in _invalidDomains)
        {
            Console.WriteLine("invalid: " + domain);
        }
        
    }
    private enum Method
    {
        GET,
        POST,
        DELETE
    }
    
}