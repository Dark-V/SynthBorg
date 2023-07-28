using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using System.IO.Compression;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using SynthBorg;
using System.Diagnostics;
using System.Text;
using System.Security.Policy;
using System.Text.RegularExpressions;

namespace SynthBorg
{
    public partial class Form1 : Form
    {
        private const long MaxLogFileSize = 1073741824; // Maximum log file size in bytes (128 MB)
        static readonly string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SynthBorg");
        static string configPath = Path.Combine(logDirectory,  "config.json");
        static string logFilePath = Path.Combine(logDirectory, "log.txt");

        private List<string> messageHistory = new List<string>(); // Maintain a list to store the message history
        private int messageIndex = -1; // Track the index of the current message in the history
        private bool WebSocketInfo = true;

        private SpeechSynthesizer synthesizer;

        private TcpClient ircClient;
        private StreamReader reader;
        private StreamWriter writer;

        public Form1()
        {
            // Initialize log writer
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);

                using (File.Create(configPath)) {}
                using (File.Create(logFilePath)) {}
            }

            InitializeComponent();
            InitializeVoices();
        }

        private void InitializeTTV()
        {
            string channel = channelBox.Text; // darkv__
            string token = "oauth:" + tokenBox.Text; // oauth:t7db2pnotdwis66x4h6orvbtc9l49i

            if (channel.Length > 0 && token.Length > 0)
                _ = AuthenticateAsync(channel, token, channel);
        }
        private void InitializeVoices()
        {
            synthesizer = new SpeechSynthesizer();
            var voices = synthesizer.GetInstalledVoices();
            var voiceNames = voices.Select(voice => voice.VoiceInfo.Name).ToList();

            voiceNames.Remove("Microsoft Zira Desktop"); // Don't support RU symbols

            cboVoices.DataSource = voiceNames;
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            LoadConfig();
            InitializeTTV();
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveConfig();
        }
        private void SaveConfig()
        {
            var config = new Config
            {
                Voice = cboVoices.SelectedValue.ToString(),
                Speed = Convert.ToInt32(cboSpeed.SelectedItem),
                IgnoredUsers = GetIgnoredUsers(),
                WhitelistedUsers = GetWhitelistedUsers(),
                channel = channelBox.Text,
                token = tokenBox.Text,
                websocketinfo = WebSocketInfo,
                mod = checkBox1.Checked,
                sub = checkBox2.Checked,
                vip = checkBox3.Checked,
            };

            var json = JsonConvert.SerializeObject(config);
            File.WriteAllText(configPath, json);
        }
        private void LoadConfig()
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<Config>(json);

                if (config != null)
                {
                    cboVoices.SelectedItem = cboVoices.Items.Cast<string>().FirstOrDefault(item => item.Contains(config.Voice));
                    cboSpeed.SelectedItem = cboSpeed.Items.Cast<string>().FirstOrDefault(item => item.Contains(config.Speed.ToString()));
                    channelBox.Text = config.channel;
                    tokenBox.Text = config.token;
                    WebSocketInfo = config.websocketinfo;
                    checkBox1.Checked = config.mod;
                    checkBox2.Checked = config.sub;
                    checkBox3.Checked = config.vip;
                }
            }
        }
        private void UpdateLogTextBox(string log)
        {
            if (log_textBox.InvokeRequired) log_textBox.Invoke(new Action<string>(UpdateLogTextBox), log);
            else log_textBox.AppendText(log + Environment.NewLine);
        }

        private void txtMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true; // Prevent the ding sound
                btnSay.PerformClick(); // Invoke the click event of the button

                txtMessage.Text = "";

                // Reset the message index after sending a message
                messageIndex = -1;
            }
            else if (e.KeyCode == Keys.Up)
            {
                e.Handled = true; // Prevent the cursor from moving to the beginning of the text

                if (messageIndex < messageHistory.Count - 1)
                {
                    // Increment the message index to retrieve the previous message
                    messageIndex++;
                    txtMessage.Text = messageHistory[messageHistory.Count - 1 - messageIndex];
                }
            }
        }
        public void LogMessage(string message)
        {
            string logMessage = $"[INFO] {DateTime.Now}: {message}";

            using (StreamWriter logWriter = new StreamWriter(logFilePath, true))
            {
                logWriter.WriteLine(logMessage);
            };

            CheckLogFileSize();
            UpdateLogTextBox(logMessage);
        }

        public void LogError(string error)
        {
            string logError = $"[ERROR] {DateTime.Now}: {error}";

            using (StreamWriter logWriter = new StreamWriter(logFilePath, true))
            {
                logWriter.WriteLine(logError);
            }

            CheckLogFileSize();
            UpdateLogTextBox(logError);
        }
        private void CheckLogFileSize()
        {
            // log_textBox.Text += "Checking log file size";

            FileInfo logFileInfo = new FileInfo(logFilePath);

            if (logFileInfo.Exists && logFileInfo.Length >= MaxLogFileSize)
            {
                // log_textBox.Text += "logWriter is being flushed";

                string tempFilePath = Path.Combine(logDirectory, "temp");
                File.WriteAllText(tempFilePath, File.ReadAllText(logFilePath));

                File.WriteAllText(logFilePath, string.Empty);

                // Compress the temporary file to old_log.zip
                string oldLogZipPath = Path.Combine(logDirectory, "old_log.zip");
                try
                {
                    using (FileStream zipToCreate = new FileStream(oldLogZipPath, FileMode.Create))
                    {
                        using (ZipArchive archive = new ZipArchive(zipToCreate, ZipArchiveMode.Create))
                        {
                            ZipArchiveEntry logEntry = archive.CreateEntry(Path.GetFileName(tempFilePath));
                            using (StreamWriter writer = new StreamWriter(logEntry.Open()))
                            {
                                using (StreamReader reader = new StreamReader(tempFilePath))
                                {
                                    writer.Write(reader.ReadToEnd());
                                }
                            }
                        }
                    }
                }
                catch (IOException ex)
                {
                    // Handle the exception gracefully (e.g., display an error message)
                    MessageBox.Show($"Failed to compress log file: {ex.Message}") ;
                }

                // Remove the temporary file
                File.Delete(tempFilePath);
            }
        }

        private async Task AuthenticateAsync(string nick, string token, string channel)
        {
            try
            {
                LogMessage("Starting T.TV authenticating");
                ircClient = new TcpClient("irc.chat.twitch.tv", 80);

                reader = new StreamReader(ircClient.GetStream());
                writer = new StreamWriter(ircClient.GetStream());

                await writer.WriteLineAsync($"CAP REQ :twitch.tv/commands twitch.tv/tags");
                await writer.WriteLineAsync($"PASS {token}");
                await writer.WriteLineAsync($"NICK {nick}");
                await writer.WriteLineAsync($"JOIN #{channel}");
                await writer.FlushAsync();

                LogMessage("Starting listening messages from T.TV chat");

                // Start a separate task to periodically send pings
                var pingCancellationTokenSource = new CancellationTokenSource();
                var pingTask = SendPingAsync(pingCancellationTokenSource.Token);

                await ListenForMessagesAsync();

                // Stop the ping task when ListenForMessagesAsync completes
                pingCancellationTokenSource.Cancel();
                await pingTask;
            }
            catch (Exception ex)
            {
                LogError($"Error AuthenticateAsync: {ex.Message}");
            }
        }

        private async Task SendPingAsync(CancellationToken cancellationToken)
        {
            var pingInterval = TimeSpan.FromSeconds(30);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Send the ping message to the server
                    await writer.WriteLineAsync("PING :tmi.twitch.tv");
                    await writer.FlushAsync();
                    await Task.Delay(pingInterval, cancellationToken);
                }
                catch (Exception ex)
                {
                    LogError($"Error sending ping: {ex.Message}");
                }
            }
        }

        private async Task ListenForMessagesAsync()
        {
            try
            {
                while (true)
                {
                    string data = await reader.ReadLineAsync();
                    if (!string.IsNullOrEmpty(data))
                    {
                        if (WebSocketInfo) LogMessage(data);

                        if (data.Contains("PRIVMSG"))
                        {
                            await ProcessMessageAsync(data);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private string GetFieldValue(string tags, string field)
        {
            try
            {
                string[] tagPairs = tags.Split(';');

                foreach (string tagPair in tagPairs)
                {
                    string[] tagParts = tagPair.Split('=');
                    if (tagParts.Length == 2 && tagParts[0] == field)
                    {
                        return tagParts[1];
                    }
                }
            }
            catch (Exception) { }

            return null;
        }
        private async Task ProcessMessageAsync(string message)
        {
            try
            {
                int lastColonIndex = message.LastIndexOf(':');
                if (lastColonIndex >= 0 && lastColonIndex + 1 < message.Length)
                {
                    string msgPayload = message.Substring(lastColonIndex + 1);
                    string tags = message.Substring(0, lastColonIndex);

                    if (msgPayload.StartsWith("!say"))
                    {
                        // Extract the text to be spoken    
                        string text = msgPayload.Substring(5);

                        bool is_mod = GetFieldValue(tags, "mod") == "1";
                        bool is_sub = GetFieldValue(tags, "subscriber") == "1";
                        bool is_vip = GetFieldValue(tags, "vip") != null;
                        // LogMessage($"is_mod={is_mod}; is_vip={is_vip}; is_sub={is_sub};");

                        string display_name = GetFieldValue(tags, "display-name");

                        bool canSpeak = false;

                        if (checkBox1.Checked && is_mod) canSpeak = true;
                        if (checkBox2.Checked && is_vip) canSpeak = true;
                        if (checkBox3.Checked && is_sub) canSpeak = true;

                        if (IsWhitelistedUser(display_name)) canSpeak = true;

                        if (canSpeak) await SpeakAsync(text);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error processing message: {ex.Message}");
            }
        }

        private bool IsWhitelistedUser(string username)
        {
            List<string> whitelistedUsers = GetWhitelistedUsers();
            return whitelistedUsers.Contains(username);
        }

        private async Task SpeakAsync(string message)
        {
            try
            {
                if (synthesizer != null && !string.IsNullOrWhiteSpace(message))
                {
                    // Get the selected voice and speed values
                    string selectedVoice = cboVoices.SelectedItem.ToString();
                    int selectedSpeed = int.TryParse(cboSpeed.SelectedItem?.ToString(), out int speed) ? speed : 1;

                    // Set the voice and speed
                    synthesizer.SelectVoice(selectedVoice);
                    synthesizer.Rate = selectedSpeed;

                    await Task.Run(() => synthesizer.SpeakAsync(message));
                    LogMessage("Spoke: " + message);
                }
            }
            catch (Exception ex)
            {
                LogError(ex.ToString());
                MessageBox.Show("An error occurred while test speaking the message.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void btnSay_Click(object sender, EventArgs e)
        {
            string message = txtMessage.Text.Trim();

            if (string.IsNullOrWhiteSpace(message)) return;

            messageHistory.Add(message);

            if (message.StartsWith("/"))
            {
                string[] commandParts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string command = commandParts[0].ToLower();

                switch (command)
                {
                    case "/clear":
                        if (commandParts.Length > 0)
                        {
                            log_textBox.Text = "";
                        }
                        break;

                    case "/save":
                            if (commandParts.Length > 0)
                            {
                                SaveConfig();
                                LogMessage($"Sucessfuly saved your data.");
                            }
                        break;

                    case "/ttv_info":
                        if (commandParts.Length > 0)
                        {
                            WebSocketInfo = !WebSocketInfo;
                        }
                        break;

                    case "/testit":
                        if (commandParts.Length > 0)
                        {
                            await SpeakAsync("Жопа. Укуси мой блестящий железный зад!");
                        }
                        break;

                    case "/reload":
                        if (commandParts.Length > 0)
                        {
                            log_textBox.Text = "";

                            reader?.Close();
                            writer?.Close();
                            ircClient?.Close();

                            InitializeTTV();
                        }
                        break;

                    case "/say":
                        if (commandParts.Length > 1)
                        {
                            string text = string.Join(" ", commandParts.Skip(1));
                            await SpeakAsync(text);
                        }
                        break;

                    case "/ignore":
                        if (commandParts.Length > 1)
                        {
                            string username = commandParts[1];
                            await AddIgnoredUserAsync(username);
                        }
                        break;

                    case "/pardon":
                        if (commandParts.Length > 1)
                        {
                            string username = commandParts[1];
                            await RemoveIgnoredUserAsync(username);
                        }
                        break;

                    case "/blocklist":
                        if (commandParts.Length > 0)
                        {
                            List<string> ignoredUsers = GetIgnoredUsers();
                            string ignoredUsersList = string.Join(Environment.NewLine, ignoredUsers);
                            log_textBox.Text += "Current list of blocked users:" + Environment.NewLine + ignoredUsersList + Environment.NewLine;
                        }
                        break;

                    case "/whitelist":
                        if (commandParts.Length > 1)
                        {
                            string username = commandParts[1];
                            await AddWhitelistedUserAsync(username);
                        }
                        break;

                    case "/unwhitelist":
                        if (commandParts.Length > 1)
                        {
                            string username = commandParts[1];
                            await RemoveWhitelistedUserAsync(username);
                        }
                        break;

                    case "/allowlist":
                        if (commandParts.Length > 0)
                        {
                            List<string> whitelistedUsers = GetWhitelistedUsers();
                            string whitelistedUsersList = string.Join(Environment.NewLine, whitelistedUsers);
                            log_textBox.Text += "Current list of whitelisted users:" + Environment.NewLine + whitelistedUsersList + Environment.NewLine;
                        }
                        break;
                    case "/help":
                        if (commandParts.Length > 0)
                        {
                            string helpText = "Доступные команды:" + Environment.NewLine +
                                "/clear - Очистить окно вывода" + Environment.NewLine +
                                "/ttv_info - Включает или выключает подробный вывод TTV" + Environment.NewLine +
                                "/testit - Произнести тестовое сообщение" + Environment.NewLine +
                                "/reload - Перезагрузить подклбчение к TTV" + Environment.NewLine +
                                "/say [сообщение] - Произнести указанное сообщение" + Environment.NewLine +
                                "/ignore [имя_пользователя] - Игнорировать указанного пользователя" + Environment.NewLine +
                                "/pardon [имя_пользователя] - Удалить пользователя из списка игнорируемых" + Environment.NewLine +
                                "/blocklist - Показать текущий список заблокированных пользователей" + Environment.NewLine +
                                "/whitelist [имя_пользователя] - Добавить указанного пользователя в белый список" + Environment.NewLine +
                                "/unwhitelist [имя_пользователя] - Удалить указанного пользователя из белого списка" + Environment.NewLine +
                                "/save - Сохранить текущие настройки конфигурации" + Environment.NewLine +
                                "/allowlist - Показать текущий список разрешенных пользователей";

                            log_textBox.Text += helpText + Environment.NewLine;
                        }
                        break;

                    default:
                        LogError(command + " is an invalid command.");
                        break;
                }
            }
        }

        private async Task AddIgnoredUserAsync(string userId)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Read the existing config file
                    string json = File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<Config>(json) ?? new Config();

                    // Initialize the ignored users list if it is null
                    if (config.IgnoredUsers == null)
                    {
                        config.IgnoredUsers = new List<string>();
                    }

                    if (config.IgnoredUsers.Contains(userId))
                    {
                        LogMessage($"This user already exists in the ignore list: {userId}");
                        return;
                    }

                    // Add the user ID to the ignored users list
                    config.IgnoredUsers.Add(userId);

                    // Serialize the updated config object
                    json = JsonConvert.SerializeObject(config);

                    File.WriteAllText(configPath, json);
                    LogMessage($"Ignored user: {userId}");
                }
                catch (Exception ex)
                {
                    LogError($"Error adding ignored user: {ex.Message}");
                }
            });
        }
        private async Task RemoveIgnoredUserAsync(string userId)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Read the existing config file
                    string json = File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<Config>(json) ?? new Config();

                    if (!config.IgnoredUsers.Contains(userId))
                    {
                        LogMessage($"This user does not exist in the ignore list: {userId}");
                        return;
                    }

                    // Remove the user ID from the ignored users list
                    config.IgnoredUsers?.Remove(userId);

                    // Serialize the updated config object
                    json = JsonConvert.SerializeObject(config);
                    File.WriteAllText(configPath, json);

                    LogMessage($"Pardoned user: {userId}");
                }
                catch (Exception ex)
                {
                    LogError($"Error removing ignored user: {ex.Message}");
                }
            });
        }

        private async Task AddWhitelistedUserAsync(string userId)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Read the existing config file
                    string json = File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<Config>(json) ?? new Config();

                    // Initialize the whitelisted users list if it is null
                    if (config.WhitelistedUsers == null)
                    {
                        config.WhitelistedUsers = new List<string>();
                    }

                    if (config.WhitelistedUsers.Contains(userId))
                    {
                        LogMessage($"This user already exists in the whitelist: {userId}");
                        return;
                    }

                    // Add the user ID to the whitelisted users list
                    config.WhitelistedUsers.Add(userId);

                    // Serialize the updated config object
                    json = JsonConvert.SerializeObject(config);

                    File.WriteAllText(configPath, json);
                    LogMessage($"Whitelisted user: {userId}");
                }
                catch (Exception ex)
                {
                    LogError($"Error adding whitelisted user: {ex.Message}");
                }
            });
        }

        private async Task RemoveWhitelistedUserAsync(string userId)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Read the existing config file
                    string json = File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<Config>(json) ?? new Config();

                    if (!config.WhitelistedUsers.Contains(userId))
                    {
                        LogMessage($"This user does not exist in the whitelist: {userId}");
                        return;
                    }

                    // Remove the user ID from the whitelisted users list
                    config.WhitelistedUsers?.Remove(userId);

                    // Serialize the updated config object
                    json = JsonConvert.SerializeObject(config);
                    File.WriteAllText(configPath, json);

                    LogMessage($"Removed user from whitelist: {userId}");
                }
                catch (Exception ex)
                {
                    LogError($"Error removing whitelisted user: {ex.Message}");
                }
            });
        }

        private List<string> GetIgnoredUsers()
        {
            try
            {
                string json = File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<Config>(json);
                return config?.IgnoredUsers ?? new List<string>();
            }
            catch (Exception ex)
            {
                LogError($"Error reading ignored users: {ex.Message}");
                return new List<string>();
            }
        }

        private List<string> GetWhitelistedUsers()
        {
            try
            {
                string json = File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<Config>(json);
                return config?.WhitelistedUsers ?? new List<string>();
            }
            catch (Exception ex)
            {
                LogError($"Error reading whitelisted users: {ex.Message}");
                return new List<string>();
            }
        }
        private void buttonTokenGen_Click(object sender, EventArgs e)
        {
            string urlf = "https://id.twitch.tv/oauth2/authorize?response_type=token&client_id=livbg1uzwm54a5wjkbqaht5l60elv3&redirect_uri=http://localhost:3000&scope=chat:read+chat:edit";

            DialogResult result = MessageBox.Show("Do you want to generate new token?", "Token generation", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

            if (result == DialogResult.OK)
            {
                MessageBox.Show("You need to copy your ACCESS_TOKEN " + Environment.NewLine +
                    "For example http://localhost:3000/#access_token=xxqagi8o1czkv6vjffff9s33g0pfwj&scope=chat%3Aread+chat%3Aedit&token_type=bearer"
                    + Environment.NewLine + "So that you're key: \"xxqagi8o1czkv6vjffff9s33g0pfwj\"");

                Process.Start(new ProcessStartInfo(urlf) { UseShellExecute = true });
            }
                

        }
    }

    public class Config
    {
        public string Voice { get; set; }
        public bool websocketinfo { get; set; }
        public bool mod { get; set; }
        public bool sub { get; set; }
        public bool vip { get; set; }
        public string channel { get; set; }
        public string token { get; set; }
        public int Speed { get; set; }
        public List<string> IgnoredUsers { get; set; }
        public List<string> WhitelistedUsers { get; set; }
    }
}
