// See https://aka.ms/new-console-template for more information
using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Azure.AI.DocumentIntelligence;
using System;
using System.IO;
using Microsoft.Extensions.Configuration;


namespace azureocr
{
    class Program
    {
        private static IConfiguration? _configuration;

        static void Main(string[] args)
        {
            // 載入配置文件
            _configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            if(args.Length != 2)
            {
                Console.WriteLine("image path required.");
                return;
            }

            if(args[0] != "vision" && args[0] != "DI")
            {
                Console.WriteLine("vision or DI required.");
                return;
            }

            if(!File.Exists(args[1]))
            {
                Console.WriteLine("image path not found. " + args[1]);
                return;
            }

            if(args[0] == "vision")
            {
                vision(args[1]);    
            }
            else if(args[0] == "DI")
            {
                di(args[1]);
            }
        }

        static void vision(string imagePath)
        {
            string endpoint = _configuration!["AzureVision:Endpoint"]!;
            string key = _configuration!["AzureVision:Key"]!;

            // Create an Image Analysis client.
            ImageAnalysisClient client = new ImageAnalysisClient(new Uri(endpoint), new AzureKeyCredential(key));

            using FileStream stream = new FileStream(imagePath, FileMode.Open);

            // Extract text (OCR) from an image stream.
            ImageAnalysisResult result = client.Analyze(
                BinaryData.FromStream(stream),
                VisualFeatures.Read);

            // Print text (OCR) analysis results to the console
            Console.WriteLine("Image analysis results:");
            Console.WriteLine(" Read:");

            foreach (DetectedTextBlock block in result.Read.Blocks)
            {
                foreach (DetectedTextLine line in block.Lines)
                {
                    Console.WriteLine($"   Line: '{line.Text}', Bounding Polygon: [{string.Join(" ", line.BoundingPolygon)}]");
                    foreach (DetectedTextWord word in line.Words)
                    {
                        Console.WriteLine($"     Word: '{word.Text}', Confidence {word.Confidence.ToString("#.####")}, Bounding Polygon: [{string.Join(" ", word.BoundingPolygon)}]");
                    }
                }
            }
        }

        static void di(string imagePath)
        {
            string endpoint = _configuration!["AzureDocumentIntelligence:Endpoint"]!;
            string key = _configuration!["AzureDocumentIntelligence:Key"]!;

            // Create a Document Intelligence client.
            DocumentIntelligenceClient client = new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(key));

            using FileStream stream = new FileStream(imagePath, FileMode.Open);

            // Start the analysis of the document
            var operation = client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-layout", BinaryData.FromStream(stream));
            AnalyzeResult result = operation.Result.Value;

            foreach (DocumentPage page in result.Pages)
            {
                Console.WriteLine($"Document Page {page.PageNumber} has {page.Lines.Count} line(s), {page.Words.Count} word(s)," +
                    $" and {page.SelectionMarks.Count} selection mark(s).");

                for (int i = 0; i < page.Lines.Count; i++)
                {
                    DocumentLine line = page.Lines[i];

                    Console.WriteLine($"  Line {i}:");
                    Console.WriteLine($"    Content: '{line.Content}'");

                    Console.Write("    Bounding polygon, with points ordered clockwise:");
                    for (int j = 0; j < line.Polygon.Count; j += 2)
                    {
                        Console.Write($" ({line.Polygon[j]}, {line.Polygon[j + 1]})");
                    }

                    Console.WriteLine();
                }

                for (int i = 0; i < page.SelectionMarks.Count; i++)
                {
                    DocumentSelectionMark selectionMark = page.SelectionMarks[i];

                    Console.WriteLine($"  Selection Mark {i} is {selectionMark.State}.");
                    Console.WriteLine($"    State: {selectionMark.State}");

                    Console.Write("    Bounding polygon, with points ordered clockwise:");
                    for (int j = 0; j < selectionMark.Polygon.Count; j++)
                    {
                        Console.Write($" ({selectionMark.Polygon[j]}, {selectionMark.Polygon[j + 1]})");
                    }

                    Console.WriteLine();
                }
            }

            for (int i = 0; i < result.Paragraphs.Count; i++)
            {
                DocumentParagraph paragraph = result.Paragraphs[i];

                Console.WriteLine($"Paragraph {i}:");
                Console.WriteLine($"  Content: {paragraph.Content}");

                if (paragraph.Role != null)
                {
                    Console.WriteLine($"  Role: {paragraph.Role}");
                }
            }

            foreach (DocumentStyle style in result.Styles)
            {
                // Check the style and style confidence to see if text is handwritten.
                // Note that value '0.8' is used as an example.

                bool isHandwritten = style.IsHandwritten.HasValue && style.IsHandwritten == true;

                if (isHandwritten && style.Confidence > 0.8)
                {
                    Console.WriteLine($"Handwritten content found:");

                    foreach (DocumentSpan span in style.Spans)
                    {
                        var handwrittenContent = result.Content.Substring(span.Offset, span.Length);
                        Console.WriteLine($"  {handwrittenContent}");
                    }
                }
            }

            for (int i = 0; i < result.Tables.Count; i++)
            {
                DocumentTable table = result.Tables[i];

                Console.WriteLine($"Table {i} has {table.RowCount} rows and {table.ColumnCount} columns.");

                foreach (DocumentTableCell cell in table.Cells)
                {
                    Console.WriteLine($"  Cell ({cell.RowIndex}, {cell.ColumnIndex}) is a '{cell.Kind}' with content: {cell.Content}");
                }
            }

        }
    }
}