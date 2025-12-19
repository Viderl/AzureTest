// See https://aka.ms/new-console-template for more information
using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Azure.AI.DocumentIntelligence;
using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using SkiaSharp;


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
                }
            }

            // 在圖片上繪製辨識結果
            DrawTextBoxesOnImage(imagePath, result);
        }

        static void DrawTextBoxesOnImage(string imagePath, ImageAnalysisResult result)
        {
            // 載入原始圖片
            using var bitmap = SKBitmap.Decode(imagePath);
            using var canvas = new SKCanvas(bitmap);

            // 設定紅色框線的畫筆
            using var boxPaint = new SKPaint
            {
                Color = SKColors.Red,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3,
                IsAntialias = true
            };

            // 設定文字的畫筆
            using var textPaint = new SKPaint
            {
                Color = SKColors.Red,
                TextSize = 20,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("PMingLiU", SKFontStyle.Bold)
            };

            // 設定文字背景
            using var backgroundPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 200), // 半透明白色
                Style = SKPaintStyle.Fill
            };

            // 遍歷所有辨識到的文字區塊
            foreach (DetectedTextBlock block in result.Read.Blocks)
            {
                foreach (DetectedTextLine line in block.Lines)
                {
                    // 繪製邊界框
                    var path = new SKPath();
                    var points = line.BoundingPolygon;
                    
                    if (points.Count >= 4)
                    {
                        path.MoveTo((float)points[0].X, (float)points[0].Y);
                        for (int i = 1; i < points.Count; i++)
                        {
                            path.LineTo((float)points[i].X, (float)points[i].Y);
                        }
                        path.Close();
                        canvas.DrawPath(path, boxPaint);
                    }

                    // 在框的上方繪製文字
                    float textX = (float)points[0].X;
                    float textY = (float)points[0].Y - 5;

                    // 測量文字大小以繪製背景
                    SKRect textBounds = new SKRect();
                    textPaint.MeasureText(line.Text, ref textBounds);
                    
                    // 繪製文字背景
                    SKRect bgRect = new SKRect(
                        textX,
                        textY + textBounds.Top - 2,
                        textX + textBounds.Width + 4,
                        textY + textBounds.Bottom + 2
                    );
                    canvas.DrawRect(bgRect, backgroundPaint);

                    // 繪製文字
                    canvas.DrawText(line.Text, textX + 2, textY, textPaint);

                    path.Dispose();
                }
            }

            // 保存結果圖片
            string outputPath = Path.Combine(
                Path.GetDirectoryName(imagePath)!,
                Path.GetFileNameWithoutExtension(imagePath) + "_result.png"
            );

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var outputStream = File.OpenWrite(outputPath);
            data.SaveTo(outputStream);

            Console.WriteLine($"\n結果圖片已保存至: {outputPath}");
        }

        static void di(string imagePath)
        {
            string endpoint = _configuration!["AzureDocumentIntelligence:Endpoint"]!;
            string key = _configuration!["AzureDocumentIntelligence:Key"]!;

            // Create a Document Intelligence client.
            DocumentIntelligenceClient client = new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(key));

            // 讀取檔案為 BinaryData
            using FileStream stream = new FileStream(imagePath, FileMode.Open);
            BinaryData fileData = BinaryData.FromStream(stream);

            // Start the analysis of the document
            var operation = client.AnalyzeDocument(WaitUntil.Completed, "prebuilt-layout", fileData);
            AnalyzeResult result = operation.Value;

            // 檢查是否為圖片檔案
            string extension = Path.GetExtension(imagePath).ToLower();
            bool isImage = extension == ".jpg" || extension == ".jpeg" || extension == ".png" || 
                          extension == ".bmp" || extension == ".tiff" || extension == ".tif";

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
                    for (int j = 0; j < selectionMark.Polygon.Count-1; j++)
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

            // 如果是圖片檔案，繪製辨識結果
            if (isImage)
            {
                DrawDITextBoxesOnImage(imagePath, result);
            }
        }

        static void DrawDITextBoxesOnImage(string imagePath, AnalyzeResult result)
        {
            // 載入原始圖片
            using var bitmap = SKBitmap.Decode(imagePath);
            using var canvas = new SKCanvas(bitmap);

            // 設定紅色框線的畫筆
            using var boxPaint = new SKPaint
            {
                Color = SKColors.Red,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3,
                IsAntialias = true
            };

            // 設定文字的畫筆
            using var textPaint = new SKPaint
            {
                Color = SKColors.Red,
                TextSize = 20,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("PMingLiU", SKFontStyle.Bold)
            };

            // 設定文字背景
            using var backgroundPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 200), // 半透明白色
                Style = SKPaintStyle.Fill
            };

            // 遍歷所有頁面
            foreach (DocumentPage page in result.Pages)
            {
                // Document Intelligence 的座標是以英吋為單位
                // 需要根據頁面尺寸和實際圖片尺寸來計算縮放比例
                float scaleX = bitmap.Width / (float)page.Width!.Value;
                float scaleY = bitmap.Height / (float)page.Height!.Value;
                
                Console.WriteLine($"圖片尺寸: {bitmap.Width}x{bitmap.Height}, 頁面尺寸: {page.Width}x{page.Height}");
                Console.WriteLine($"縮放比例: X={scaleX}, Y={scaleY}");
                
                // 遍歷所有辨識到的文字行
                foreach (DocumentLine line in page.Lines)
                {
                    // 繪製邊界框
                    var path = new SKPath();
                    var polygon = line.Polygon;
                    
                    if (polygon.Count >= 8) // 確保有4個點（8個座標值）
                    {
                        // Document Intelligence 的 Polygon 格式是 [x1, y1, x2, y2, x3, y3, x4, y4]
                        // 座標以英吋為單位，需要轉換為像素
                        path.MoveTo((float)polygon[0] * scaleX, (float)polygon[1] * scaleY);
                        path.LineTo((float)polygon[2] * scaleX, (float)polygon[3] * scaleY);
                        path.LineTo((float)polygon[4] * scaleX, (float)polygon[5] * scaleY);
                        path.LineTo((float)polygon[6] * scaleX, (float)polygon[7] * scaleY);
                        path.Close();
                        canvas.DrawPath(path, boxPaint);

                        // 在框的上方繪製文字
                        float textX = (float)polygon[0] * scaleX;
                        float textY = (float)polygon[1] * scaleY - 5;

                        // 測量文字大小以繪製背景
                        SKRect textBounds = new SKRect();
                        textPaint.MeasureText(line.Content, ref textBounds);
                        
                        // 繪製文字背景
                        SKRect bgRect = new SKRect(
                            textX,
                            textY + textBounds.Top - 2,
                            textX + textBounds.Width + 4,
                            textY + textBounds.Bottom + 2
                        );
                        canvas.DrawRect(bgRect, backgroundPaint);

                        // 繪製文字
                        canvas.DrawText(line.Content, textX + 2, textY, textPaint);

                        path.Dispose();
                    }
                }
            }

            // 保存結果圖片
            string directory = Path.GetDirectoryName(imagePath)!;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(imagePath);
            string extension = Path.GetExtension(imagePath);
            string outputPath = Path.Combine(directory, fileNameWithoutExt + "-di" + extension);

            using var image = SKImage.FromBitmap(bitmap);
            
            // 根據原始檔案格式選擇編碼格式
            SKEncodedImageFormat format = extension.ToLower() switch
            {
                ".png" => SKEncodedImageFormat.Png,
                ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                ".bmp" => SKEncodedImageFormat.Bmp,
                _ => SKEncodedImageFormat.Png
            };

            using var data = image.Encode(format, 100);
            using var outputStream = File.OpenWrite(outputPath);
            data.SaveTo(outputStream);

            Console.WriteLine($"\n結果圖片已保存至: {outputPath}");
        }
    }
}