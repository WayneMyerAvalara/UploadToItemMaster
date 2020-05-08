using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalara.AvaTax.RestClient;

namespace ItemMasterLoad
{
    class Program
    {
        //These constants need to be changed to your values.
        private const string USERNAME = "Your Username Here";
        private const string PASSWORD = "Your Password Here";
        private const string PATH_TO_CSV = "Full path to your CSV file goes here";
        
        //Change this value to your company ID.
        private const int COMPANY_ID = 123456;

        //Which environment do you wish to use? Change this value for Sandbox or Production.
        private const AvaTaxEnvironment avaTaxEnvironment = AvaTaxEnvironment.Sandbox;

        static void Main(string[] args)
        {
            //Instantiate REST client
            var client =
                new AvaTaxClient("ItemMasterUpload", "YourAppVersionHere",
                    "yourMachineNameHere", avaTaxEnvironment)
            .WithSecurity(USERNAME, PASSWORD);

            var itemModels = new List<ItemModel>();

            //Load a list of Classification Systems into memory for easy lookup.
            var classificationSystems = client.ListProductClassificationSystems(string.Empty, null, null, string.Empty).value;

            //Open CSV 
            OpenAndParseCsv(itemModels, classificationSystems);

            UpsertItems(client, itemModels);
        }

        private static void UpsertItems(AvaTaxClient client, List<ItemModel> itemModels)
        {
            foreach (ItemModel newItemModel in itemModels)
            {
                try
                {
                    //Does the item already exist in this company?
                    var existingItem = client.QueryItems(string.Format("itemCode EQ {0} AND companyId EQ {1}", newItemModel.itemCode, COMPANY_ID), string.Empty, null, null, string.Empty).value.FirstOrDefault();

                    //Yes, the item exists. Load the HS Codes to this item.
                    if (existingItem != null)
                    {
                        var existingClassifications = client.ListItemClassifications(COMPANY_ID, existingItem.id, string.Empty, null, null, string.Empty).value;

                        foreach (var cm in newItemModel.classifications)
                        {
                            ItemClassificationInputModel clsInputModel = new ItemClassificationInputModel { productCode = cm.productCode, systemCode = cm.systemCode };

                            //Does the classification already exist? And is it the same?
                            var sameClassificationSystem = existingClassifications.Where(ec => ec.systemCode == cm.systemCode).FirstOrDefault();
                            var exactSameClassification = existingClassifications.Where(ec => ec.systemCode == cm.systemCode && ec.productCode == cm.productCode).FirstOrDefault();

                            //Classification does not exist. Add it.
                            if (sameClassificationSystem == null)
                            {
                                client.CreateItemClassifications(COMPANY_ID, existingItem.id, new List<ItemClassificationInputModel> { clsInputModel });

                                continue;
                            }

                            //Classification exists, but is different. Update it.
                            if (sameClassificationSystem != null &&
                                exactSameClassification == null)
                            {
                                client.UpdateItemClassification(COMPANY_ID, existingItem.id, sameClassificationSystem.id.Value, clsInputModel);
                            }
                        }
                    }
                    else
                    {
                        //Item does not yet exist. Create a new Item.
                        client.CreateItems(COMPANY_ID, new List<ItemModel>() { newItemModel });
                    }
                }
                catch (AvaTaxError exc)
                {
                    Console.WriteLine(string.Format("Error loading/updating item {0}", newItemModel.id));
                    Console.WriteLine(string.Format("More information: {0}", exc.Message));
                }
                catch (Exception exc)
                {
                    Console.WriteLine(exc.Message);
                }
            }
        }

        private static void OpenAndParseCsv(List<ItemModel> itemModels, List<ProductClassificationSystemModel> classificationSystems)
        {
            using (var reader = new StreamReader(PATH_TO_CSV))
            {
                var header = reader.ReadLine();
                var headerValues = header.Split(",");

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var values = line.Split(',');
                    var classifications = new List<ClassificationModel>();


                    for (int i = 5; i < values.Count() - 1; i++)
                    {
                        var ps = classificationSystems.Where(cs => cs.countries.Any(p => p.country == headerValues[i])).FirstOrDefault();
                        ClassificationModel classification = new ClassificationModel() { systemCode = ps.systemCode, productCode = values[i] };

                        if (classifications.Any(cs => cs.systemCode == classification.systemCode))
                        {
                            continue;
                        }

                        classifications.Add(classification);
                    }

                    ItemModel item = new ItemModel()
                    {
                        itemCode = values[0],
                        description = values[1],
                        companyId = COMPANY_ID,
                        classifications = classifications
                    };

                    itemModels.Add(item);
                }
            }
        }
    }
}
