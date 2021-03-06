﻿using System;
using System.IO;
//#r "C:\Users\Tom\.nuget\packages\unirest-api\1.0.7.6\lib\unirest-net.dll"
using unirest_net.http;
//#r "C:\Users\Tom\.nuget\packages\newtonsoft.json\7.0.1\lib\net45\Newtonsoft.Json.dll"
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Configuration;
using System.Collections.Generic;
//#r "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Net\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Net.dll"
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using Microsoft.Azure.Cosmos;
using Azure.Storage.Blobs;
//using PropSearchConsole;
using System.Collections;


namespace PropSearch
{
    class Program
    {
        JObject parameters = JObject.Parse(System.IO.File.ReadAllText(@"E:\DataStore\PropSearchConsole\parameters.txt"));

        // The Cosmos client instance
        private CosmosClient cosmosClient;

        // Create a BlobServiceClient object which will be used to create a container client
        private BlobClient blobClient;


        // The database we will create
        private Database database;

        // The container we will create.
        private Container cosmosContainer;

        private async Task CreateDatabaseAsync()
        {
            // Create a new database
            this.database = await this.cosmosClient.CreateDatabaseIfNotExistsAsync((string)parameters["databaseId"]);
            Console.WriteLine("Created Database: {0}\n", this.database.Id);
        }

        private async Task CreateContainerAsync()
        {
            // Create a new container
            this.cosmosContainer = await this.database.CreateContainerIfNotExistsAsync((string)parameters["containerId"], "/ListingID");
            Console.WriteLine("Created Container: {0}\n", this.cosmosContainer.Id);
        }

        private async Task UpdatePriceAsync(string ListingID, int Price)
        {
            ItemResponse<Property> LocalQueryResponse = await this.cosmosContainer.ReadItemAsync<Property>(ListingID, new PartitionKey(ListingID));
            var itemBody = LocalQueryResponse.Resource;
            int oldprice = itemBody.Price;
            itemBody.Price = Price;
            LocalQueryResponse = await this.cosmosContainer.ReplaceItemAsync<Property>(itemBody, itemBody.id, new PartitionKey(itemBody.id));
            Console.WriteLine("Updated {0} from {1} to {2}\n", ListingID, oldprice, Price);
        }

        private async Task UpdatecityAsync(string ListingID, string City)
        {
            Console.WriteLine(ListingID); 
            ItemResponse<Property> LocalQueryResponse = await this.cosmosContainer.ReadItemAsync<Property>(ListingID, new PartitionKey(ListingID));
            var itemBody = LocalQueryResponse.Resource;
            itemBody.City = City;
            LocalQueryResponse = await this.cosmosContainer.ReplaceItemAsync<Property>(itemBody, itemBody.id, new PartitionKey(itemBody.id));
            Console.WriteLine("Updated {0} to {1}\n", ListingID, City); 
        }

        private async Task UpdateCityAsync(BlobClient blobClient)
        {
            string GetRead = "read";
            dynamic responseJSON2 = JsonConvert.DeserializeObject(GetInfoAsync(GetRead, blobClient).Result);
            foreach (var property in responseJSON2)
            {
                string ListingID = property["listing_id"];
                string City = "No City";
                try { City = (property["address"]["city"]).ToString(); }
                catch { City = "No City"; }
                await UpdatecityAsync(ListingID, City);
            }
        }

        private static async Task<string> GetInfoAsync(string getread, BlobClient blobClient)
        {
            string response = "";
            if (getread == "get")
            {
                string cities = "Nanjemoy,Welcome,Port Tobacco,Marbury,Holly Haven";
                string[] citiesArray = cities.Split(',');
                foreach (var city in citiesArray)
                {
                    Console.WriteLine("Getting {0}\r",city);
                    var client = new HttpClient();
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Get,
                        RequestUri = new Uri("https://realtor.p.rapidapi.com/properties/v2/list-for-sale?city=" + city + "&limit=200&offset=0&state_code=MD"),
                        Headers =
                        {
                            { "X-RapidAPI-Host", "realtor.p.rapidapi.com" },
                            { "X-RapidAPI-Key", "37e55441e9msh666c920aa1b90a1p1ea11cjsn7020db0de8f5"},
                        },
                    };
                    var info = await client.SendAsync(request);
                    dynamic responseJSON = JsonConvert.DeserializeObject(await info.Content.ReadAsStringAsync());

                    if (responseJSON["meta"]["returned_rows"] > 0)
                    {
                        responseJSON = responseJSON["properties"];
                        response = response + JsonConvert.SerializeObject(responseJSON);
                    }
                }
                response = response.Replace("][", ",");
                blobClient.Delete();
                var content = Encoding.UTF8.GetBytes(response);
                using (var ms = new MemoryStream(content))
                    blobClient.Upload(ms);
                //System.IO.File.WriteAllText(@"E:\DataStore\PropSearchConsole\currentProps.txt", response);
            }
            else if (getread == "read")
            {
                var download = await blobClient.DownloadAsync();
                using (var streamReader = new StreamReader(download.Value.Content))
                {
                    response = await streamReader.ReadLineAsync();
                }
                //response = System.IO.File.ReadAllText(@"E:\DataStore\PropSearchConsole\currentProps.txt");
            }
            return response;
        }

        private async Task AddItemsToContainerAsync(BlobClient blobClient)
        {
            Console.WriteLine("get or read");
            string GetRead = Console.ReadLine();
            //string GetRead = "get";
            dynamic responseJSON2 = JsonConvert.DeserializeObject(GetInfoAsync(GetRead, blobClient).Result);
            double RTUs = 0;
            List<string> ActivePropIDs = new List<string>();
            foreach (var property in responseJSON2)
            {
                string AddressLine = "No Address Line";
                string proptype = "No Property Type";
                string rdc_web_url = "No RDC Web URL";
                string city = "No City";
                double lotSize = 0;
                int price = 0;
                try   { lotSize = ConvertSize((property["lot_size"]["units"]).ToString(), (property["lot_size"]["size"]).ToObject<double>()); }
                catch { lotSize = 0; }
                try   { price = (property["price"].ToObject<int>()); }
                catch { price = 0; }
                try   { AddressLine = (property["address"]["line"]).ToString(); }
                catch { AddressLine = "No Address Line";  }
                try   { city = GetAddressLine((property["address"]["city"]).ToString()); }
                catch { city = "No City"; }
                try   { proptype = property["prop_type"]; }                
                catch { proptype = "No Property Type"; }
                try   { rdc_web_url = property["rdc_web_url"]; }
                catch { rdc_web_url = "No RDC Web URL"; }

                //Console.WriteLine(property);
                Property CurrentProperty = new Property
                {
                    id = property["listing_id"],
                    ListingID = property["listing_id"],
                    PropertyID = property["property_id"],
                    City = city,
                    AddressLine = AddressLine,
                    PropType = proptype,
                    Price = price,
                    LotSize = lotSize,
                    RDC_web_url = rdc_web_url
                };

                try
                {
                    // Read the item to see if it exists.  
                    ItemResponse<Property> ExistingProperty = await this.cosmosContainer.ReadItemAsync<Property>(CurrentProperty.ListingID, new PartitionKey(CurrentProperty.ListingID));
                    if (ExistingProperty.Resource.Price == CurrentProperty.Price)
                    {
                        Console.BackgroundColor = ConsoleColor.Black;
                    }
                    else if (CurrentProperty.Price > ExistingProperty.Resource.Price)
                    {
                        Console.BackgroundColor = ConsoleColor.Red;
                        await UpdatePriceAsync(CurrentProperty.ListingID, CurrentProperty.Price);
                    }
                    else
                    {
                        Console.BackgroundColor = ConsoleColor.Green;
                        //Console.WriteLine("{0},{1}\r", CurrentProperty.ListingID, CurrentProperty.Price);
                        await UpdatePriceAsync(CurrentProperty.ListingID, CurrentProperty.Price);
                    }
                    Console.WriteLine("Old item~{0},{1},{2},{3},{4},{5},{6},{7}\r",
                        ExistingProperty.Resource.ListingID,
                        ExistingProperty.Resource.PropertyID,
                        ExistingProperty.Resource.City,
                        ExistingProperty.Resource.AddressLine,
                        ExistingProperty.Resource.LotSize,
                        ExistingProperty.Resource.PropType,
                        ExistingProperty.Resource.Price,
                        ExistingProperty.Resource.RDC_web_url);
                    ActivePropIDs.Add(CurrentProperty.id);
                    RTUs = RTUs + ExistingProperty.RequestCharge;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // Create an item in the container representing the property record. Note we provide the value of the partition key for this item, which is "ListingID
                    ItemResponse<Property> NewProperty = await this.cosmosContainer.CreateItemAsync<Property>(CurrentProperty, new PartitionKey(CurrentProperty.ListingID));

                    // Note that after creating the item, we can access the body of the item with the Resource property off the ItemResponse. We can also access the RequestCharge property to see the amount of RUs consumed on this request.
                    Console.BackgroundColor = ConsoleColor.Black; 
                    Console.WriteLine("New item~{0},{1},{2},{3},{4},{5},{6},{7}\r",
                        NewProperty.Resource.ListingID,
                        NewProperty.Resource.PropertyID,
                        NewProperty.Resource.City,
                        NewProperty.Resource.AddressLine,
                        NewProperty.Resource.LotSize,
                        NewProperty.Resource.PropType,
                        NewProperty.Resource.Price,
                        NewProperty.Resource.RDC_web_url);
                    ActivePropIDs.Add(NewProperty.Resource.id);
                    RTUs = RTUs + NewProperty.RequestCharge;
                }
            }
            Console.WriteLine("Total RTUs: {0}\n\r", RTUs);
            Console.ReadLine();


            //Delete old properties
            List<Property> AllLines = new List<Property>();
            AllLines.AddRange(await RunQuery("SELECT p.ListingID, p.AddressLine FROM Properties p"));
            foreach (Property line in AllLines)
            {
                if(!ActivePropIDs.Contains(line.ListingID)) 
                {
                    var partitionKeyValue = line.ListingID;
                    ItemResponse<Property> DeleteProperty = await this.cosmosContainer.DeleteItemAsync<Property>(line.ListingID, new PartitionKey(partitionKeyValue));
                    Console.WriteLine("Removed: {0},{1}\r",
                      line.ListingID,
                      line.AddressLine);
                }
            }
            Console.WriteLine();
        }
        private async Task<List<Property>> RunQuery(string sqlQueryText)
        {

            QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);
            FeedIterator<Property> queryResultSetIterator = this.cosmosContainer.GetItemQueryIterator<Property>(queryDefinition);

            List<Property> Lines = new List<Property>();

            while (queryResultSetIterator.HasMoreResults)
            {
                FeedResponse<Property> currentResultSet = await queryResultSetIterator.ReadNextAsync();
                foreach (Property line in currentResultSet)
                {

                    Lines.Add(line);
                    //Console.WriteLine("{2}\t{1}\t{0}", line.AddressLine, line.LotSize, line.Price);
                }
            }
            return (Lines);
        }
        private async Task CreateReportsAsync()
        {
            
            List<Property> AllLines = new List<Property>();
            string body = "";

            AllLines.AddRange(await RunQuery("SELECT p.ListingID, p.AddressLine, p.City, p.Price, p.LotSize, p.RDC_web_url " +
                "FROM Properties p " +
                "WHERE p.AddressLine LIKE '%Spencer%' OR p.AddressLine LIKE '%Tayl%'"));
            Console.WriteLine("Properties on Spencer or Tayloes Neck:");
            body = body + "<p>Properties on Spencer or Tayloes Neck:<br>";
            foreach (Property line in AllLines)
            {
                string reportline = 
                    line.ListingID + "," +
                    line.City + "," +
                    line.AddressLine + "," +
                    line.LotSize + "," +
                    line.Price + "," +
                    line.RDC_web_url + "</br>";
                Console.WriteLine("{0}\r", reportline);
                body = body + reportline;
            }
            body = body + "</p>";
            Console.WriteLine();
            

            List<Property> AllLines1 = new List<Property>();
            AllLines1.AddRange(await RunQuery("SELECT p.ListingID, p.AddressLine, p.City, p.Price, p.LotSize, p.RDC_web_url " +
                "FROM Properties p " +  
                "WHERE p.LotSize >= 5 ORDER BY p.LotSize ASC"));
            body = body + "Properties 5+ Acres:</br>";
            Console.WriteLine("Properties 5+ Acres:");
            foreach (Property line in AllLines1)
            {
                string reportline =
                    line.ListingID + "," +
                    line.City + "," +
                    line.AddressLine + "," +
                    line.LotSize + "," +
                    line.Price + "," +
                    line.RDC_web_url + "</br>";
                Console.WriteLine("{0}\r", reportline);
                body = body + reportline;
            }
            body = body + "</p>";
            await SendMessage(body);
        }


        public static double ConvertSize(string units, double size)
        {
            //Console.WriteLine(units + size);
            double lotSize = 0;
            if (units == "sqft")
            {
                lotSize = Math.Round((size / 43560), 2);
            }
            else
            {
                lotSize = size;
            }
            return lotSize;
        }

        public static string GetAddressLine(string AddressLine)
        {
            if (AddressLine != null)
            {
                return AddressLine;
            }
            else
            {
                return "No Address";
            }
        }

        private async Task SendMessage(string body)
        {
            MailMessage msg = new MailMessage();
            msg.To.Add(new MailAddress("tom@tomfranken.com", "Tom Franken"));
            msg.From = new MailAddress("tom@tomfranken.com", "Tom Franken");
            msg.Subject = "Land Report";
            msg.Body = body;
            msg.IsBodyHtml = true;
            SmtpClient client = new SmtpClient();
            client.UseDefaultCredentials = false;
            string apppwd = (string)parameters["apppwd"];
            client.Credentials = new System.Net.NetworkCredential("tom@tomfranken.com", apppwd);//#insert your credentials
			client.Port = 587;
            client.Host = "smtp.office365.com";
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.EnableSsl = true;
            try
            {
                client.Send(msg);
                Console.WriteLine("Email Successfully Sent");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }


        public async Task ProcessInfo()
        {
            /*this.cosmosClient = new CosmosClient((string)parameters["EndpointUri"], (string)parameters["PrimaryKey"]);
            this.cosmosContainer = this.cosmosClient.GetContainer((string)parameters["databaseId"], (string)parameters["cosmosContainerId"]);
            this.blobClient = new BlobClient((string)parameters["blobConnectionString"], (string)parameters["blobContainerName"], (string)parameters["blobName"]);
            */
            this.cosmosClient = new CosmosClient(Environment.GetEnvironmentVariable("EndpointUri"), Environment.GetEnvironmentVariable("PrimaryKey"));
            this.cosmosContainer = this.cosmosClient.GetContainer(Environment.GetEnvironmentVariable("databaseId"), Environment.GetEnvironmentVariable("cosmosContainerId"));
            this.blobClient = new BlobClient(Environment.GetEnvironmentVariable("blobConnectionString"), Environment.GetEnvironmentVariable("blobContainerName"), Environment.GetEnvironmentVariable("blobName"));
            

            Console.WriteLine("Set clients");
            //await this.CreateDatabaseAsync();
            //await this.CreateContainerAsync();
            await this.AddItemsToContainerAsync(this.blobClient);
            //await this.UpdateCityAsync(this.blobClient);
            await this.CreateReportsAsync();
            //await this.DeleteItemAsync();
            Console.Read();
        }

        public static async Task Main(string[] args)
        {
            Program p = new Program();
            
            await p.ProcessInfo();
        }
    }
}
