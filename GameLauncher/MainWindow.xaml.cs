using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace GameLauncher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string VERSION_URL = "https://your-cdn.r2.dev/version.json"; // R2 linki!
        private const string APP_NAME = "YourApp";
        private const string MAIN_EXE = "YourApp.exe";

        private readonly string _localAppData;
        private readonly string _appDirectory;
        private readonly string _versionFile;
        private readonly HttpClient _httpClient;
        private Storyboard _spinnerAnimation;

        public MainWindow()
        {
            InitializeComponent();

            _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _appDirectory = Path.Combine(_localAppData, APP_NAME);

        }
    }
}