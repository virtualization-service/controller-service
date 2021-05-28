using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ControllerService.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Parser.Model;

namespace ControllerService.Processors
{
    public class DuplicationContext
    {
        private readonly List<string> _ignoredHeaders = new List<string>{"host","content-type", "content-length"};

        private readonly MessageDto messageDto = new MessageDto{ request = new Body{ headers = new Dictionary<string, string>()}, response = new Body{headers = new Dictionary<string, string>()}};

        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => { return true; }
        });
        private string dataSaverUrl = Environment.GetEnvironmentVariable("DATA_SAVER_URL") ?? "http://datasaver-vavatar-test.apps.azureneocp.neinnovation-ocp.com/api/data/recordOperations";

        private static List<RecordingDto> _recordings = new List<RecordingDto>();

        public async Task<MessageDto> InvokeService(Microsoft.AspNetCore.Http.HttpContext context, string resource)
        {

            var id = resource.Split('/')[2];
            var matchingResource = _recordings.FirstOrDefault(x => x.VirtualEndpoint.Contains(id));
            if (matchingResource == null) RefreshList();

            matchingResource = _recordings.FirstOrDefault(x => x.VirtualEndpoint.Contains(id));
            var serviceUrl = new Uri(matchingResource.ServiceEndpoint).Scheme + "://" + new Uri(matchingResource.ServiceEndpoint).Host + resource.Replace("record/" + id + "/", "");
            if (context.Request.QueryString != null)
            {
                serviceUrl = serviceUrl + context.Request.QueryString.ToString();
            }

            messageDto.service = new Uri(serviceUrl);

            await Invoke(context, new Uri(serviceUrl), messageDto);

            return messageDto;
        }


        private void RefreshList()
        {
            var client = new HttpClient();
            var response = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, dataSaverUrl)).GetAwaiter().GetResult();

            _recordings = (JsonConvert.DeserializeObject<RecordingDto[]>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())).ToList();

        }

        public async Task Invoke(HttpContext context, Uri targetUri, MessageDto messageDto)
        {
            if (targetUri != null)
            {
                var targetRequestMessage = CreateTargetMessage(context, targetUri);

                using (var responseMessage = await _httpClient.SendAsync(targetRequestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted))
                {
                    context.Response.StatusCode = (int)responseMessage.StatusCode;
                    CopyFromTargetResponseHeaders(context, responseMessage);

                    var responseByteStream = responseMessage.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    messageDto.response.raw_data = System.Text.Encoding.Default.GetString(responseByteStream);
                    await new MemoryStream(responseByteStream).CopyToAsync(context.Response.Body);

                }
                return;
            }
        }

        private HttpRequestMessage CreateTargetMessage(HttpContext context, Uri targetUri)
        {
            var requestMessage = new HttpRequestMessage();
            CopyFromOriginalRequestContentAndHeaders(context, requestMessage);

            requestMessage.RequestUri = targetUri;
            requestMessage.Headers.Host = targetUri.Host;
            requestMessage.Method = GetMethod(context.Request.Method);

            return requestMessage;
        }

        private  void CopyFromOriginalRequestContentAndHeaders(HttpContext context, HttpRequestMessage requestMessage)
        {
            var requestMethod = context.Request.Method;

            if (!HttpMethods.IsGet(requestMethod) &&
              !HttpMethods.IsHead(requestMethod) &&
              !HttpMethods.IsDelete(requestMethod) &&
              !HttpMethods.IsTrace(requestMethod))
            {
                var streamByteContent = new StreamContent(context.Request.Body).ReadAsByteArrayAsync().GetAwaiter().GetResult();

                Stream ms = new MemoryStream(streamByteContent);
                //ms.Write(streamByteContent, 0, streamByteContent.Length);

                messageDto.request.raw_data = System.Text.Encoding.Default.GetString(streamByteContent);

                var streamContent = new StreamContent(context.Request.Body);

                requestMessage.Content = new StreamContent(ms);// streamContent;
            }

            foreach (var header in context.Request.Headers)
            {
                if(!_ignoredHeaders.Contains(header.Key.ToLower()) ) messageDto.request.headers.TryAdd(header.Key, header.Value.ToString());
                requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        private void CopyFromTargetResponseHeaders(HttpContext context, HttpResponseMessage responseMessage)
        {
            foreach (var header in responseMessage.Headers)
            {
                if(! _ignoredHeaders.Contains(header.Key.ToLower())) messageDto.request.headers.TryAdd(header.Key, header.Value.ToString());

                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            foreach (var header in responseMessage.Content.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            context.Response.Headers.Remove("transfer-encoding");
        }
        private static HttpMethod GetMethod(string method)
        {
            if (HttpMethods.IsDelete(method)) return HttpMethod.Delete;
            if (HttpMethods.IsGet(method)) return HttpMethod.Get;
            if (HttpMethods.IsHead(method)) return HttpMethod.Head;
            if (HttpMethods.IsOptions(method)) return HttpMethod.Options;
            if (HttpMethods.IsPost(method)) return HttpMethod.Post;
            if (HttpMethods.IsPut(method)) return HttpMethod.Put;
            if (HttpMethods.IsTrace(method)) return HttpMethod.Trace;
            return new HttpMethod(method);
        }
    }
}