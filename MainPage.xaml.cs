using static MTRApp.MainPage;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Drawing;

namespace MTRApp
{
    public partial class MainPage : ContentPage
    {
        int count = 0;
        ImageSource imageSource;
        byte[] image;
        public MainPage()
        {
            InitializeComponent();
            Loading.IsRunning = false;
        }

        private async void OnCounterClicked(object sender, EventArgs e)
        {

            SemanticScreenReader.Announce(CounterBtn.Text);
            if (MediaPicker.Default.IsCaptureSupported)
            {
                FileResult photo = await MediaPicker.Default.CapturePhotoAsync();
                if (photo != null)
                {
                    // save the file into local storage
                    string localFilePath = Path.Combine(FileSystem.CacheDirectory, photo.FileName);
                    MemoryStream memoryStream;
                    using (Stream sourceStream = await photo.OpenReadAsync())
                    {
                        memoryStream = new MemoryStream();
                        await sourceStream.CopyToAsync(memoryStream);
                        memoryStream.Position = 0; // Reset the position of MemoryStream to the start
                        image = memoryStream.ToArray();
                        imageSource = ImageSource.FromStream(() => memoryStream);
                    }

                    // display the photo
                    PickedImage.Source = ImageSource.FromStream(() => memoryStream);
                }
            }

        }

        public async Task<FileResult> PickAndShow(PickOptions options)
        {
            try
            {
                var result = await FilePicker.Default.PickAsync(options);
                if (result != null)
                {
                    if (result.FileName.EndsWith("jpg", StringComparison.OrdinalIgnoreCase) ||
                        result.FileName.EndsWith("png", StringComparison.OrdinalIgnoreCase))
                    {
                        using var stream = await result.OpenReadAsync();
                        var image = ImageSource.FromStream(() => stream);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                // The user canceled or something went wrong
            }

            return null;
        }


        private async void OnPickImageButtonClicked(object sender, EventArgs e)
        {
            await PickAndShowImage();
        }

        private async Task PickAndShowImage()
        {
            FileResult fileResult = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Please pick an image",
                FileTypes = FilePickerFileType.Images,
            });

            if (fileResult != null)
            {
                image = await File.ReadAllBytesAsync(fileResult.FullPath);
#if ANDROID
                // Resize image on Android
                using var stream = new MemoryStream(image);
                var bitmap = Android.Graphics.BitmapFactory.DecodeStream(stream);
                float originalWidth = bitmap.Width;
                float originalHeight = bitmap.Height;
                float targetWidth = 600;
                float targetHeight = targetWidth * (originalHeight / originalWidth);
                var resizedBitmap = Android.Graphics.Bitmap.CreateScaledBitmap(bitmap, (int)targetWidth, (int)targetHeight, true);
                using var ms = new MemoryStream();
                resizedBitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg, 100, ms);
                image = ms.ToArray();
#elif IOS || MACCATALYST
                //// Resize image on iOS and Mac Catalyst
                //var uiImage = new UIKit.UIImage(Foundation.NSData.FromArray(image));
                //float originalWidth = (float)uiImage.Size.Width;
                //float originalHeight = (float)uiImage.Size.Height;
                //float targetWidth = 600;
                //float targetHeight = targetWidth * (originalHeight / originalWidth);
                //UIGraphics.BeginImageContext(new CoreGraphics.CGSize(targetWidth, targetHeight));
                //uiImage.Draw(new CoreGraphics.CGRect(0, 0, targetWidth, targetHeight));
                //var resizedImage = UIGraphics.GetImageFromCurrentImageContext();
                //UIGraphics.EndImageContext();
                //image = resizedImage.AsJPEG().ToArray();
#elif WINDOWS
                // Resize image on Windows
                using var stream = new MemoryStream(image);
                using var originalImage = new Bitmap(stream);
                float originalWidth = originalImage.Width;
                float originalHeight = originalImage.Height;
                float targetWidth = 600;
                float targetHeight = targetWidth * (originalHeight / originalWidth);
                var resizedImage = new Bitmap(originalImage, new System.Drawing.Size((int)targetWidth, (int)targetHeight));
                using var ms = new MemoryStream();
                resizedImage.Save(ms, originalImage.RawFormat);
                image = ms.ToArray();
#endif

                imageSource = ImageSource.FromStream(() => new MemoryStream(image));
                PickedImage.Source = imageSource;
            }
        }

        private async void Predict(object sender, EventArgs e)
        {
            Loading.IsRunning = true;
            if (imageSource is not null)
            {
                HttpClient client = new HttpClient();
                client.BaseAddress = new Uri("https://1ptmbrt4-7101.uks1.devtunnels.ms/");
                // Create a MultipartFormDataContent
                var multiContent = new MultipartFormDataContent();

                // Add the image to the form data as a ByteArrayContent
                multiContent.Add(new ByteArrayContent(image), "file", "file.jpg");


                //post photo to server as an IFormFile
                var response = await client.PostAsync("UploadImage", multiContent);
                if (!response.IsSuccessStatusCode)
                {
                    await Application.Current.MainPage.DisplayAlert("Error", "Error uploading image", "OK");
                    Loading.IsRunning = false;
                    return;
                }
                var prediction = await response.Content.ReadFromJsonAsync<YoloNet>();

                //get image and set it to PickedImage.Source
                PickedImage.Source = ImageSource.FromStream(() => new MemoryStream(prediction.YoloResult.Image));

                var score = prediction.NetResult.Score.Max() * 100;
                if (score > 90.0)
                    await Application.Current.MainPage.DisplayAlert($"Prediction -{score}%", prediction.NetResult.PredictedLabel, "OK");
                else if (score > 50.0)
                    await Application.Current.MainPage.DisplayAlert($"Prediction -{score}%", $"Not sure what this is, confidence does not meet threashold. But it may be a {prediction.NetResult.PredictedLabel}.", "OK");
                else
                    await Application.Current.MainPage.DisplayAlert($"Prediction -{score}%", "Not sure what this is, confidence does not meet threashold.", "OK");


                if (prediction.YoloResult.Detections.Count > 0)
                {
                    var detection = "Object \t\t Confidence";
                    var detection2 = "Object \t\t Confidence";
                    foreach (var item in prediction.YoloResult.Detections.Where(prediction => prediction.Confidence > 0.9))
                    {
                        detection += $"\n{item.Object} \t\t {item.Confidence}";
                    }
                    foreach (var item in prediction.YoloResult.Detections.Where(prediction => prediction.Confidence <= 0.9))
                    {
                        detection2 += $"\n{item.Object} \t\t {item.Confidence}";
                    }
#if WINDOWS
                    await Application.Current.MainPage.DisplayAlert($"Detected", $"Confidently detections found.\n\n {detection} \n\n Other detections found.\n\n {detection2}", "OK");
#endif
                    YoloText.Text = detection;
                }
                Loading.IsRunning = false;
            }
        }
    }
    public class Prediction
    {
        public int Label { get; set; }
        public byte[] ImageSource { get; set; }
        public string PredictedLabel { get; set; }
        public List<double> Score { get; set; }
    }
    public class YoloNet
    {
        public Prediction NetResult { get; set; }
        public Detection YoloResult { get; set; }
    }

    public class DetectedObject
    {
        public List<double> Bbox { get; set; }
        public double Confidence { get; set; }
        public string Object { get; set; }
    }
    public class Detection
    {
        public List<DetectedObject> Detections { get; set; }
        public byte[] Image { get; set; }
    }
}
