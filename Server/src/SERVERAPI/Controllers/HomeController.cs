﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.NodeServices;
using SERVERAPI.Models;
using SERVERAPI.Models.Impl;
using SERVERAPI.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Http;
using System.Net.Http;
using System.Text;
using System.Net;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Hosting;
using System.Reflection;
using System.IO;
using Microsoft.Extensions.FileProviders;

namespace SERVERAPI.Controllers
{
    public class JSONResponse
    {
        public string type;
        public byte[] data;
    }
    public class PDFRequest
    {
        public string html;
    }
    public static class SessionExtensions
    {
        public static void SetObjectAsJson(this ISession session, string key, object value)
        {
            session.SetString(key, JsonConvert.SerializeObject(value));
        }

        public static T GetObjectFromJson<T>(this ISession session, string key)
        {
            var value = session.GetString(key);

            return value == null ? default(T) : JsonConvert.DeserializeObject<T>(value);
        }
    }
    public class HomeController : Controller
    {
        private IHostingEnvironment _env;

        public HomeController(IHostingEnvironment env)
        {
            _env = env;
        }
        public IActionResult Index()
        {
            return View();
        }
        public IActionResult Launch(string id)
        {
            ViewBag.Title = "NMP";
            LoadStatic();
            LaunchViewModel lvm = new LaunchViewModel();
            lvm.userData = null;

            if (id == "false")
            {
                lvm.canContinue = false;
            }
            else
            {
                lvm.canContinue = true;
            }

            HttpContext.Session.SetObjectAsJson("FarmData", lvm.userData);

            return View(lvm);

        }
        [HttpPost]
        public IActionResult Launch(LaunchViewModel lvm, string type)
        {
            if(type == "Continue")
            {
                // save the local storage user data in it's stringified form
                HttpContext.Session.SetString("FarmData", lvm.userData);
                var farmData = HttpContext.Session.GetObjectFromJson<FarmData>("FarmData");
            }
            else
            {
                FarmData userData = new FarmData();
                HttpContext.Session.SetObjectAsJson("FarmData", userData);
                var farmData = HttpContext.Session.GetObjectFromJson<FarmData>("FarmData");
            }
            return RedirectToAction("Farm","Farm");
        }

        [HttpGet]
        public IActionResult Print()
        {
            var userData = HttpContext.Session.GetObjectFromJson<FarmData>("FarmData");

            FileContentResult result = null;
            //JSONResponse result = null;
            var pdfHost = Environment.GetEnvironmentVariable("PDF_SERVICE_NAME");

            //string pdfHost = Configuration["PDF_SERVICE_NAME"];

            string targetUrl = pdfHost + "/api/PDF/BuildPDF";

            ViewBag.Service = targetUrl;

            // call the microservice
            try
            {
                PDFRequest req = new PDFRequest();

                HttpClient client = new HttpClient();

                string rawdata = "<!DOCTYPE html><html><head><title></title></head><body><div style='width: 100%; background-color:lightgreen'>Section 1</div><br><div style='page -break-after:always; '></div><div style='width: 100%; background-color:lightgreen'>Section 2</div></body></html>";

                req.html = rawdata;

                string payload = JsonConvert.SerializeObject(req);

                var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                request.Headers.Clear();
                // transfer over the request headers.
                foreach (var item in Request.Headers)
                {
                    string key = item.Key;
                    string value = item.Value;
                    request.Headers.Add(key, value);
                }

                Task<HttpResponseMessage> responseTask = client.SendAsync(request);
                responseTask.Wait();

                HttpResponseMessage response = responseTask.Result;

                ViewBag.StatusCode = response.StatusCode.ToString();

                if (response.StatusCode == HttpStatusCode.OK) // success
                {
                    var bytetask = response.Content.ReadAsByteArrayAsync();
                    bytetask.Wait();

                    result = new FileContentResult(bytetask.Result, "application/pdf");
                }
            }
            catch (Exception e)
            {
                result = null;
            }

            return result;
        }
        private static async Task<JSONResponse> BuildReport(INodeServices nodeServices)
        {
            JSONResponse result = null;
            var options = new { format = "letter", orientation = "landscape" };

            var opts = new
            {
                orientation = "landscape",

            };

            string rawdata = "<!DOCTYPE html><html><head><meta charset='utf-8' /><title></title></head><body><div style='width: 100%; background-color:lightgreen'>Section 1</div><br><div style='page -break-after:always; '></div><div style='width: 100%; background-color:lightgreen'>Section 2</div></body></html>";

            // execute the Node.js component
            result = await nodeServices.InvokeAsync<JSONResponse>("pdf.js", rawdata, options);

            return result;
        }
        public void LoadStatic()
        {
            var assembly = Assembly.GetEntryAssembly();
            var resourceStream = assembly.GetManifestResourceStream("SERVERAPI.Data.static.json");
            using (var reader = new StreamReader(resourceStream, Encoding.UTF8))
            {
                string staticValues = reader.ReadToEnd();
                HttpContext.Session.Set("Static", Encoding.ASCII.GetBytes(staticValues));
            }
        }
        public IActionResult Download()
        {
            var farmData = HttpContext.Session.GetObjectFromJson<FarmData>("FarmData");

            var fileName = farmData.year + " - " + farmData.farmName + ".nmp";
            byte[] fileBytes = Encoding.ASCII.GetBytes(HttpContext.Session.GetString("FarmData"));
            return File(fileBytes, "application/octet-stream", fileName);
        }
        public IActionResult FileLoad()
        {
            FileLoadViewModel lvm = new FileLoadViewModel();

            return View(lvm);
        }
        [HttpPost]
        public IActionResult FileLoad(FileLoadViewModel lvm)
        {
            string fileContents = "";

            if (Request.Form.Files.Count > 0)
            {
                if (Request.Form.Files.Count > 1)
                {
                    ModelState.AddModelError("", "Only one file may be selected.");
                }
                else
                {
                    try
                    {
                        foreach (var file in Request.Form.Files)
                        {
                            var fileBytes = new byte[file.Length];

                            file.OpenReadStream().Read(fileBytes, 0, (int)file.Length);
                            fileContents = System.Text.Encoding.Default.GetString(fileBytes);
                        }

                        try
                        {
                            FarmData fd = JsonConvert.DeserializeObject<FarmData>(fileContents);
                        }
                        catch(Exception ex)
                        {
                            ModelState.AddModelError("", "File does not appear to be a valid NMP data file.");
                            return View(lvm);
                        }

                        // Returns message that successfully uploaded  
                        HttpContext.Session.SetString("FarmData", fileContents);

                        string url = Url.Action("Farm", "Farm");
                        return Json(new { success = true, url = url, farmdata = fileContents });
                    }
                    catch (Exception ex)
                    {
                        return Json("Error occurred. Error details: " + ex.Message);
                    }
                }
            }
            else
            {
                ModelState.AddModelError("", "A file hs not been selected.");
            }
            return View(lvm);
        }
        [HttpGet]
        public IActionResult SoilTests()
        {
            var farmData = HttpContext.Session.GetObjectFromJson<FarmData>("FarmData");
            Models.Impl.StaticData sd = new Models.Impl.StaticData();

            FarmViewModel fvm = new FarmViewModel();

            return View(fvm);
        }
        [HttpPost]
        public IActionResult SoilTests(FarmViewModel fvm)
        {

            return View(fvm);
        }
        [HttpGet]
        public IActionResult Manure()
        {
            var farmData = HttpContext.Session.GetObjectFromJson<FarmData>("FarmData");
            Models.Impl.StaticData sd = new Models.Impl.StaticData();

            FarmViewModel fvm = new FarmViewModel();

            return View(fvm);
        }
        [HttpPost]
        public IActionResult Manure(FarmViewModel fvm)
        {

            return View(fvm);
        }
    }
}
