using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace RackSpaceCLI;

public class RestApiClient
{
    private readonly string apiKey;
    private readonly string baseUrl;
    private HttpWebRequest request;
    private readonly string secretKey;

    public RestApiClient(string baseUrl, string apiKey, string secretKey)
    {
        this.baseUrl = baseUrl;
        this.apiKey = apiKey;
        this.secretKey = secretKey;
    }

    public async Task<HttpWebResponse> Get(string url, string format)
    {
        try
        {
            request = (HttpWebRequest)WebRequest.Create(baseUrl + url);
            request.Method = "GET";
            SignMessage();
            AssignFormat(format);
            return await GetResponseContentAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<HttpWebResponse> Post(string url, string data, string format)
    {
        request = (HttpWebRequest)WebRequest.Create(baseUrl + url);
        request.Method = "POST";
        SignMessage();
        AssignFormat(format);
        SendFormData(data);
        return await GetResponseContentAsync();
    }


    public async Task<HttpWebResponse> Delete(string url, string format)
    {
        request = (HttpWebRequest)WebRequest.Create(baseUrl + url);
        request.Method = "DELETE";
        SignMessage();
        AssignFormat(format);
        return await GetResponseContentAsync();
    }

    private void SendFormData(string data)
    {
        var encoding = new UTF8Encoding();
        var byteData = encoding.GetBytes(data);
        request.ContentType = "application/x-www-form-urlencoded";
        request.ContentLength = byteData.Length;
        var requestStream = request.GetRequestStream();
        requestStream.Write(byteData, 0, byteData.Length);
        requestStream.Close();
    }

    private async Task<HttpWebResponse> GetResponseContentAsync()
    {
        try
        {
            return (HttpWebResponse)await request.GetResponseAsync();
        }
        catch (WebException e)
        {
            return (HttpWebResponse)e.Response;
        }
    }

    private void SignMessage()
    {
        var userAgent = "C# Client Library";
        request.UserAgent = userAgent;
        var dateTime = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var dataToSign = apiKey + userAgent + dateTime + secretKey;
        var hash = SHA1.Create();
        var signedBytes = hash.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
        var signature = Convert.ToBase64String(signedBytes);

        request.Headers["X-Api-Signature"] = apiKey + ":" + dateTime + ":" + signature;
    }

    private void AssignFormat(string format)
    {
        request.Accept = format;
    }
}