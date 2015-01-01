using BruTile.Predefined;
using BruTile.Web;
using System;
using System.Linq;
using System.Net;
using System.Windows.Controls;

namespace BruTile.Demo
{
    public partial class MainWindow
    {
        private ITileSource _currentSyncSource;

        public MainWindow()
        {
            InitializeComponent();

            foreach (var knownTileSource in Enum.GetValues(typeof(KnownTileSource)).Cast<KnownTileSource>())
            {
                if (knownTileSource.ToString().ToLower().Contains("cloudmade")) continue; // Exclude CloudMade

                KnownTileSource source = knownTileSource;
                var radioButton = ToRadioButton(knownTileSource.ToString(), () => _currentSyncSource = KnownTileSources.Create(source, "soep"));
                Layers.Children.Add(radioButton);
            }

            UseAsyncOption.Checked += (sender, args) => SetTileSource(_currentSyncSource);

            Layers.Children.Add(ToRadioButton("Google Map", () =>
                _currentSyncSource = CreateGoogleTileSource("http://mt{s}.google.com/vt/lyrs=m@130&hl=en&x={x}&y={y}&z={z}")));
            Layers.Children.Add(ToRadioButton("Google Terrain", () =>
                _currentSyncSource = CreateGoogleTileSource("http://mt{s}.google.com/vt/lyrs=t@125,r@130&hl=en&x={x}&y={y}&z={z}")));
        }

        private static ITileSource CreateGoogleTileSource(string urlFormatter)
        {
            return new HttpTileSource(new GlobalSphericalMercator(), urlFormatter, new[] {"0", "1", "2", "3"},
                name: "google",
                tileFetcher: FetchImageAsGoogle()); 
        }

        private static Func<Uri, byte[]> FetchImageAsGoogle()
        {
            return uri =>
            {
                var httpWebRequest = (HttpWebRequest) WebRequest.Create(uri);
                httpWebRequest.UserAgent =
                    @"Mozilla/5.0 (Windows; U; Windows NT 6.0; en-US; rv:1.9.1.7) Gecko/20091221 Firefox/3.5.7";
                httpWebRequest.Referer = "http://maps.google.com/";
                return RequestHelper.FetchImage(httpWebRequest);
            };
        }

        private RadioButton ToRadioButton(string name, Func<ITileSource> func)
        {
            var radioButton = new RadioButton
            {
                Content = name,
                Tag = new Func<ITileSource>(func)
            };
            radioButton.Click += (sender, args) => SetTileSource(((Func<ITileSource>)((RadioButton)sender).Tag)());

            return radioButton;
        }

        private void SetTileSource(ITileSource syncTileSource)
        {
            Func<Uri, System.Threading.Tasks.Task<byte[]>> fetcher = null;
            if (syncTileSource.Name == "google")
            {
                fetcher =
                    uri =>
                    {
                        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri);
                        request.Headers.UserAgent.ParseAdd(@"Mozilla/5.0 (Windows; U; Windows NT 6.0; en-US; rv:1.9.1.7) Gecko/20091221 Firefox/3.5.7");
                        request.Headers.Referrer = new Uri("http://maps.google.com/");
                        return AsyncRequestHelper.FetchImageAsync(request)
                            .ContinueWith(bytesTask =>
                            {
                                request.Dispose();
                                return bytesTask.Result;
                            });
                    };
            }
            MapControl.SetTileSource(UseAsyncOption.IsChecked.GetValueOrDefault() ? new AsyncHttpTileSource((HttpTileSource)syncTileSource, tileFetcher: fetcher) : syncTileSource);
        }
    }
}