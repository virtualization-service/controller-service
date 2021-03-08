using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Steeltoe.CloudFoundry.Connector.RabbitMQ;
using ControllerService.Processors;
using Microsoft.AspNetCore.Http;
using System.IO;
using Parser.Model;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Xml.Linq;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace ControllerService.Processors
{
    public class RequestProcessor
    {
        public async Task ProcessResponseAsync(HttpContext context, IApplicationBuilder app, ConnectionFactory factory)
        {
            try
            {
                if (context.Request.Method == "OPTIONS")
                {
                    context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                    context.Response.Headers.Add("Access-Control-Allow-Methods", new[] { HttpMethods.Post, HttpMethods.Get, HttpMethods.Options, HttpMethods.Put, HttpMethods.Trace, HttpMethods.Delete });
                    return;
                }
                var requestPath = context.Request.Path.Value;
                Console.WriteLine($"Request Path {requestPath} and with method {context.Request.Method}");

                if (string.IsNullOrEmpty(requestPath)
                || requestPath == "/" || requestPath == "/favicon.ico" || requestPath == "/cloudfoundryapplication")
                {
                    await context.Response.WriteAsync("Running!!");
                    return;
                }

                if (requestPath == "/config")
                {
                    var environments = Environment.GetEnvironmentVariables();
                    XElement data1 = new XElement("parent");
                    var data = "";

                    foreach (var val in environments.Keys)
                    {
                        try
                        {
                            data += val.ToString() + ":" + Environment.GetEnvironmentVariable(val.ToString()) + "||";
                            //data1.Add(new XElement(val.ToString(), Environment.GetEnvironmentVariable(val.ToString())));
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                        }


                    }
                    await context.Response.WriteAsync(data.ToString());
                    return;


                }

                if (requestPath.ToLower().Equals("/virtualization-train"))
                {
                    var publisher = app.ApplicationServices.GetService<PublishMessage>();

                    var messageToPublish = await ConvertToString(context.Request?.Body);

                    if (string.IsNullOrEmpty(messageToPublish))
                    {
                        await context.Response.WriteAsync("{\"result\":\"Provide data in request body to learn\"}");
                    }
                    else
                    {
                        publisher.Publish(messageToPublish, factory);
                        await context.Response.WriteAsync("{\"result\":\"Learning is in progress, should be completed by the time you can read this.\"}");
                    }

                    return;
                }

                var headers = new Dictionary<string, string>();
                var contentType = "application/json;charset=UTF-8";

                foreach (var header in context.Request.Headers)
                {
                    if(header.Key == "Content-Type") contentType = header.Value.FirstOrDefault();
                    if (header.Key == "Content-Length") continue;


                    headers.Add(header.Key, header.Value.FirstOrDefault());
                }
                headers.Add("Method", context.Request.Method);

                var messageBody = await ConvertToString(context.Request?.Body);

                var serviceUrl = context.Request.Scheme + "://" + context.Request.Host + context.Request.Path;
                serviceUrl = QueryHelpers.AddQueryString(serviceUrl, context.Request.Query.ToDictionary(x => x.Key, y => y.Value.FirstOrDefault()));

                var message = new MessageDto
                {
                    service = new Uri(serviceUrl),
                    request = new Body
                    {
                        raw_data = messageBody,
                        headers = headers
                    }
                };

                var serializedMessage = JsonConvert.SerializeObject(message);

                Microsoft.Extensions.Primitives.StringValues auth;
                context.Request.Headers.TryGetValue("Authorization", out auth);


                Console.WriteLine($"Data is being published {serializedMessage}");
                var virtualizer = app.ApplicationServices.GetService<Virtualizer>();
                {
                    var response = await virtualizer.CallASync(serializedMessage, factory);

                    if (response == null) await context.Response.WriteAsync("Error Generating Response");

                    var jo = JObject.Parse(response);

                    var authenticationType = Convert.ToString(jo.SelectToken("serviceData.authenticationMethod"));

                    if(authenticationType == "basic"){
                        var username = Convert.ToString(jo.SelectToken("serviceData.authenticationKey"));
                        var password = Convert.ToString(jo.SelectToken("serviceData.authenticationValue"));

                        String encoded = Convert.ToBase64String(Encoding.GetEncoding("ISO-8859-1").GetBytes(username + ":" + password));

                        if("Basic " + encoded != Convert.ToString(auth)){
                             context.Response.StatusCode = 401;
                             return;
                        }

                    }

                    if(authenticationType == "token"){
                        var token = Convert.ToString(jo.SelectToken("serviceData.authenticationValue"));

                        if("Bearer " + token != Convert.ToString(auth)){
                            context.Response.StatusCode = 401;
                            return;
                        }
                    }

                    if(authenticationType == "token"){
                        var token = Convert.ToString(jo.SelectToken("serviceData.authenticationValue"));

                        if("Bearer " + token != Convert.ToString(auth)){
                            context.Response.StatusCode = 401;
                            return;
                        }
                    }

                    if(authenticationType == "api"){
                        var username = Convert.ToString(jo.SelectToken("serviceData.authenticationKey"));
                        var password = Convert.ToString(jo.SelectToken("serviceData.authenticationValue"));

                        Microsoft.Extensions.Primitives.StringValues headerKeyReceived = "";
                        context.Request.Headers.TryGetValue(username ,out headerKeyReceived);

                        if(headerKeyReceived != password){
                            context.Response.StatusCode = 401;
                            return;
                        }
                    }

                    context.Response.Headers.TryAdd("confidence", Convert.ToString(jo.SelectToken("data.confidence")));
                    context.Response.Headers.TryAdd("rank", Convert.ToString(jo.SelectToken("data.rank")));
                    context.Response.Headers.TryAdd("propertiesMatched", Convert.ToString(jo.SelectToken("data.confidence")));
                    context.Response.Headers.TryAdd("Content-Type", contentType);

                    await context.Response.WriteAsync(Convert.ToString(jo.SelectToken("data.response.raw_data")) ?? "No Data Found");
                }
            }
            catch (Exception ex)
            {

            }

        }

        private string GetQueryString(HttpRequest request)
        {
            var formattedString = string.Join("&", request.Query.Select(p => p.Key + "=" + p.Value));
            return string.IsNullOrWhiteSpace(formattedString) ? string.Empty : "?" + formattedString;
        }

        private static Task<string> ConvertToString(Stream stream)
        {
            var rdr = new StreamReader(stream);

            return rdr.ReadToEndAsync();
        }
    }
}