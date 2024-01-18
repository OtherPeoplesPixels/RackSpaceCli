using System.Net;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using nietras.SeparatedValues;
using Formatting = Newtonsoft.Json.Formatting;

namespace RackSpaceCLI;

// ** These are the rate limits RackSpace imposes on clients **
// Operation Category	Request Limit
//     GET	                                    60 per minute
//     PUT, POST, DELETE	                    30 per minute
//     POST, PUT, DELETE on a domain	        2 per minute
//     POST, DELETE on alternate domains	    2 per minute
// Enabling public folders for a domain	1 per   5 minutes

// Therefore, to mitigate 403 codes returning from the API, a wait is placed on GET and DELETE operations and a
// query limit of 30 is places on GET requests

// Delete a single mailbox
// Delete a single domain
// Delete group of domains from csv
// Delete group of mailboxes from csv

public class Program
{
    private static readonly List<string> _domains = [];

    public static async Task Main()
    {
        await LookUpCustomerByDomain();
    }


    private static async Task LookUpCustomerByDomain()
    {
        ReadCsv();
        try
        {
            const int batchSize = 200;
            const int delayTime = 60_000; // 60 seconds

            for (var i = 0; i < _domains.Count; i += batchSize)
            {
                var currentBatch = _domains.Skip(i).Take(batchSize);

                foreach (var domain in currentBatch)
                {
                    var response = await Client(domain, Method.GET);
                    using var streamReader = new StreamReader(response.GetResponseStream());
                    var json = ReaderXmlContentReturnJson(streamReader);
                    var validDomains = GetValidDomains(json);
                    Console.WriteLine($"Json: {json}");
                }

                if (i + batchSize >= _domains.Count) continue;
                Console.WriteLine("Pausing for 1 minute to satisfy rate limit...\r\n");
                await Task.Delay(delayTime);
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
            var batchSize = 3;
            var delayTime = 60_000; //60 seconds
            for (var i = 0; i < _domains.Count; i += batchSize)
            {
                var currentBatch = _domains.Skip(i).Take(batchSize);
                foreach (var domain in currentBatch)
                {
                    var response = await Client(domain, Method.DELETE);
                    using var reader = new StreamReader(response.GetResponseStream());
                    var content = await reader.ReadToEndAsync();
                    var json = GetValidDomains(content);
                    Console.WriteLine($"Json: {json}");
                    
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

    private static async Task LookupMailboxesByDomain(string domain)
    {
        try
        {
            var response = await Client(domain, Method.GET);

            using (var stream = new StreamReader(response.GetResponseStream()))
            {
                var json = ReaderXmlContentReturnJson(stream);
                Console.WriteLine(json);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private static void RemoveMailboxes(string domain)
    {
    }

    private static async Task<HttpWebResponse> Client(string domain, Method method, string data = "")
    {
        try
        {
            var rs = new RestApiClient("https://api.emailsrvr.com/", "Db45bOTFnlsOzOoO0fbr",
                "BpVIGvEneUVBFXUBt2xyRUay5dT0iygvw6XmkXCv");

            return method switch
            {
                Method.GET => await rs.Get($"customers/all/domains/{domain}",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8"),
                Method.POST => await rs.Post($"customers/all/domains/{domain}", data,
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8"),
                Method.DELETE => await rs.Delete($"customers/all/domains/{domain}",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8"),
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
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private static List<string> GetValidDomains(string jsonData)
    {
        var jsonObject = JObject.Parse(jsonData);

        if (jsonObject.ContainsKey("@code"))
        {
            return new List<string>();
        }

        if (jsonObject.ContainsKey("aliases"))
        {
            return new List<string> { jsonObject["name"].ToString() };
        }


        return jsonObject.Children()
            .OfType<JObject>()
            .Where(domain => !domain.ContainsKey("@code"))
            .Select(domain => domain["name"].ToString())
            .ToList();
    }

    private enum Method
    {
        GET,
        POST,
        DELETE
    }

    private static void ListDomains()
    {
        foreach (var domain in _domains)
        {
            Console.WriteLine($"Domain: {domain}");
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
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}