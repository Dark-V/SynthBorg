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
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;
using System.Runtime.InteropServices;
using System.Net;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SynthBorg
{
    public partial class Form1 : Form
    {
        // GLOBAL QUEUE OF SPEAKING TEXT
        private Queue<Func<Task>> taskQueue = new Queue<Func<Task>>(); 

        // settings
        private const long MaxLogFileSize = 1073741824; // Maximum log file size in bytes (1 GB)
        static readonly string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SynthBorg");
        static string configPath = Path.Combine(logDirectory,  "config.json");
        static string logFilePath = Path.Combine(logDirectory, "log.txt");
        static string ignoredWordsFilePath = Path.Combine(logDirectory, "ignoredWords.txt");
        private bool WebSocketInfo = true;

        // msg handle
        private List<string> messageHistory = new List<string>(); // Maintain a list to store the message history
        private int messageIndex = -1; // Track the index of the current message in the history

        private List<string> ignoredWords = new List<string>(); // Maintain a list all prohibted words
        private SpeechSynthesizer synthesizer;

        // TTV websocket
        private TcpClient ircClient;
        private StreamReader reader;
        private StreamWriter writer;
        private System.Windows.Forms.ToolTip toolTip;

        // Jeebot websocket
        private ClientWebSocket ws = new ClientWebSocket();
        private CancellationTokenSource cts = new CancellationTokenSource();
        private string jeebot_token = "";

        // TTV auth
        private HttpListener listener;
        public string token;

        // for !me
        private string voice_allow_msg = "ALLOWED";
        private string voice_not_allow_msg = "NOT ALLOWED";

        // hotkey code
        private const int MOD_SHIFT = 0x0004;
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 1;
        private int VK_F1 = 0x70; // F1 key code

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        public Form1()
        {
            // Check if folder exist. This work only for first run.
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);

                using (File.Create(configPath)) {}
                using (File.Create(logFilePath)) {}
                using (File.Create(ignoredWordsFilePath)) {}
            }

            if (File.Exists(ignoredWordsFilePath))
            {
                _ = ParseWordsFromFileAsync(ignoredWordsFilePath);
            }

            InitializeComponent();
            InitializeVoices();
            CheckUpdatesOnGit(false);

            toolTip = new System.Windows.Forms.ToolTip();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_HOTKEY)
            {
                int hotkeyEventId = m.WParam.ToInt32();
                if (hotkeyEventId == HOTKEY_ID)
                {
                    // F1 hotkey was pressed, trigger the event
                    stopall_btnClick(null, EventArgs.Empty);
                }
            }
        }
        public async Task<string[]> ParseWordsFromFileAsync(string ignoredWordsFilePath)
        {
            try
            {
                using (var streamReader = new StreamReader(ignoredWordsFilePath))
                {
                    var content = await Task.Run(() => streamReader.ReadToEnd()).ConfigureAwait(false);
                    ignoredWords = new List<string>(content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
                    return ignoredWords.ToArray();
                }
            }
            catch (Exception ex)
            {
                LogError($"Error when loading ignored words list: {ex.Message}");
                return new string[0];
            }
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
            if (jeebot_token.Length > 1) CheckJeeBot();

            taskProcessor = Task.Run(() => ProcessTaskQueue());

            RegisterHotKey(this.Handle, HOTKEY_ID, MOD_SHIFT, VK_F1);
        }

        private async void CheckJeeBot()
        {
            try
            {
                // Connect to the WebSocket server
                await ws.ConnectAsync(new Uri("wss://ws.jeetbot.cc/tts_websocket"), cts.Token);

                // Start a task to receive messages from the server
                _ = Task.Run(() => ReceiveMessagesAsync());

                // Prepare and send the authentication message
                await SendJeeBotMessageAsync(new
                {
                    action = "start",
                    session_id = "d22092a5-9448-47ef-ac02-b01cf198b65f",
                    auth_token = jeebot_token
                });

                LogMessage("JeeBot was succesfully booted up!");
            }
            catch (Exception ex)
            {
                LogError($"Error in CheckJeeBot: {ex.Message}");
            }
        }

        private async Task SendJeeBotMessageAsync(object message)
        {
            if (ws.State == WebSocketState.Open)
            {
                var messageJson = JsonConvert.SerializeObject(message);
                var messageBytes = Encoding.UTF8.GetBytes(messageJson);
                await ws.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, cts.Token);
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[4096]; // Start with a reasonable buffer size
            var receivedBytes = 0;
            var messageBuilder = new StringBuilder();

            while (ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    receivedBytes += result.Count;
                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var message = messageBuilder.ToString();
                        messageBuilder.Clear();
                        receivedBytes = 0; // Reset for next message

                        // Parse the JSON
                        try
                        {
                            dynamic json = JsonConvert.DeserializeObject(message);
                            if (json.file != null) ProcessAudioMessageAsync(json);
                        }
                        catch (JsonReaderException ex)
                        {
                            LogError($"Error parsing JSON: {ex.Message} - Message: {message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Error in ReceiveMessagesAsync: {ex.Message}");
                }
            }
        }

        private WaveOutEvent currentWaveOut; // Store the active WaveOutEvent
        private async Task PlayAudioAsync(byte[] audioData)
        {
            using (var mp3Reader = new Mp3FileReader(new MemoryStream(audioData)))
            {
                // Wrap Mp3FileReader in a SampleChannel
                var sampleChannel = new SampleChannel(mp3Reader);

                // Create a VolumeSampleProvider using the SampleChannel
                var volumeProvider = new VolumeSampleProvider(sampleChannel);
                volumeProvider.Volume = (volumeBar.Value / 100f) * 0.3f; // Set the desired volume

                currentWaveOut = new WaveOutEvent(); // Create a new WaveOutEvent
                currentWaveOut.Init(volumeProvider);

                currentWaveOut.Play();

                while (currentWaveOut.PlaybackState == PlaybackState.Playing)
                {
                    await Task.Delay(100);
                }

                currentWaveOut.Dispose(); // Dispose the WaveOutEvent when done
                currentWaveOut = null;
            }
        }

        private async Task ProcessAudioMessageAsync(dynamic json)
        {
            try
            {
                string base64Audio = json.file;

                // Remove the data URL prefix
                string prefix = "data:audio/mpeg;base64,";
                if (base64Audio.StartsWith(prefix))
                {
                    base64Audio = base64Audio.Substring(prefix.Length);
                }

                byte[] audioData = Convert.FromBase64String(base64Audio);
                if (!IsIgnoredUser(json.username.ToString()))
                {
                    LogMessage($"Jeebot message spoke: {json.cleaned_content.ToString()}");
                    taskQueue.Enqueue(() => PlayAudioAsync(audioData));
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in ProcessAudioMessageAsync: {ex.Message}");
            }
        }

        private Task taskProcessor;
        private async Task ProcessTaskQueue()
        {
            while (true)
            {
                if (taskQueue.Count > 0)
                {
                    var task = taskQueue.Dequeue();
                    await task(); // Await the task instead of blocking
                }
                else
                {
                    await Task.Delay(100); // Use Task.Delay to avoid blocking the thread
                }
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveConfig();
            UnregisterHotKey(this.Handle, HOTKEY_ID);
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
                voice_allow_msg = voice_allow_msg,
                voice_not_allow_msg = voice_not_allow_msg,
                hotkey = VK_F1,
                volume_slider = volumeBar.Value,
                jeebot_token = jeebot_token
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
                    voice_allow_msg = config.voice_allow_msg;
                    voice_not_allow_msg = config.voice_not_allow_msg;
                    VK_F1 = config.hotkey;
                    volumeBar.Value = config.volume_slider;
                    jeebot_token = config.jeebot_token;
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
        public void LogTextBoxOnly(string log)
        {
            log_textBox.Text += log + Environment.NewLine;

            log_textBox.Select(log_textBox.TextLength, 0);
            log_textBox.ScrollToCaret();
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
        public void SendReply(string id, string message, string channel)
        {
            string formattedMessage = $"@reply-parent-msg-id={id} PRIVMSG #{channel} :{message}\r\n";
            byte[] bytesToSend = Encoding.UTF8.GetBytes(formattedMessage);
            ircClient.GetStream().Write(bytesToSend, 0, bytesToSend.Length);
        }


        private async Task ProcessMessageAsync(string message)
        {
            try
            {
                string[] parts = message.Split(new string[] { "PRIVMSG" }, StringSplitOptions.None);
                if (parts.Length < 2) return;

                string msgPayload = parts[1].Substring(parts[1].IndexOf(':') + 1).ToLower();
                if (!msgPayload.StartsWith("!")) return;    

                // parse payload data
                string tags = parts[0];
                string display_name = GetFieldValue(tags, "display-name").ToLower();

                // LogMessage(GetFieldValue(tags, "mod"));

                bool canSpeak = CheckUserPermission(tags, display_name);

                // Command's below
                if (msgPayload.StartsWith("!! "))
                {
                    string text = msgPayload.Substring(3);
                    if (canSpeak) await SpeakAsync(text);
                }

                if (msgPayload.StartsWith("!me"))
                {
                    string message_id = GetFieldValue(tags, "id");

                    message = voice_not_allow_msg;
                    if (canSpeak) message = voice_allow_msg;

                    SendReply(message_id, message, channelBox.Text);
                }

                
            }
            catch (Exception ex)
            {
                LogError($"Error processing message: {ex.Message}");
            }
        }

        private bool CheckUserPermission(string tags, string display_name)
        {
            // permission check
            bool is_mod = GetFieldValue(tags, "mod") == "1";
            bool is_sub = GetFieldValue(tags, "subscriber") == "1";
            bool is_vip = GetFieldValue(tags, "vip") != null;
            // LogMessage($"is_mod={is_mod}; is_vip={is_vip}; is_sub={is_sub};");

            bool canSpeak = false;
            if (checkBox1.Checked && is_mod) canSpeak = true;
            if (checkBox2.Checked && is_vip) canSpeak = true;
            if (checkBox3.Checked && is_sub) canSpeak = true;

            if (channelBox.Text.ToLower() == display_name) canSpeak = true;
            if (IsWhitelistedUser(display_name)) canSpeak = true;
            if (IsIgnoredUser(display_name)) canSpeak = false;

            return canSpeak;
        }

        private bool IsWhitelistedUser(string username)
        {
            List<string> whitelistedUsers = GetWhitelistedUsers();
            return whitelistedUsers.Contains(username);
        }
        private bool IsIgnoredUser(string username)
        {
            List<string> whitelistedUsers = GetIgnoredUsers();
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
                    int selectedSpeed = int.TryParse(cboSpeed.SelectedItem?.ToString(), out int speed) ? speed : 2;

                    // Set the voice and speed
                    synthesizer.SelectVoice(selectedVoice);
                    synthesizer.Rate = selectedSpeed;
                    synthesizer.Volume = volumeBar.Value;

                    message = ReplaceIgnoredWords(message);

                    taskQueue.Enqueue(async () =>
                    {
                        try
                        {
                            await Task.Run(() => synthesizer.Speak(message));
                            LogMessage("Spoke: " + message);
                        }
                        catch (OperationCanceledException)
                        {
                            LogMessage("TTS canceled.");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogError(ex.ToString());
                MessageBox.Show("An error occurred while test speaking the message.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public string ReplaceIgnoredWords(string message)
        {
            foreach (var word in ignoredWords)
            {
                // LogMessage($"word={word} and message={message}");
                string pattern = @"\b" + Regex.Escape(word) + @"\b";
                message = Regex.Replace(message, pattern, "*", RegexOptions.IgnoreCase);
            }
            return message;
        }
        private int ShowHotkeyInputDialog()
        {
            using (var inputForm = new Form())
            {
                inputForm.StartPosition = FormStartPosition.CenterParent;
                inputForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                inputForm.Text = "Press a key to set as hotkey";
                inputForm.Width = 400;

                int hotkey = 0x70;

                inputForm.KeyDown += (sender, e) =>
                {
                    hotkey = (int)(e.KeyCode);
                    inputForm.DialogResult = DialogResult.OK;
                };

                inputForm.ShowDialog();

                return hotkey;
            }
        }
        private async void CheckUpdatesOnGit(bool isManual)
        {
            try
            {
                string url = "https://api.github.com/repos/Dark-V/SynthBorg/releases/latest";
                string returnMsg = "Что-то пошло не так...";
                bool isNeedUpdate = false;

                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "SynthBorg_API");
                    string json = client.DownloadString(url);
                    JObject release = JObject.Parse(json);

                    // Get the latest release tag (version number)
                    string latestVersion = (string)release["tag_name"];

                    if (!latestVersion.Contains("release"))
                    {
                        Version newVersion = new Version(latestVersion);
                        Version currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

                        // LogMessage(newVersion.ToString());
                        // LogMessage(currentVersion.ToString());

                        if (newVersion > currentVersion)
                        {
                            returnMsg = "Обновление доступно! Пожалуйста обновитесь";
                            isNeedUpdate = true;
                        }
                        else if (isManual) returnMsg = "Это последняя версия! UwU";
                    }
                    else
                    {
                        returnMsg = "Ваша версия устарела! Пожалуйста обновитесь!";
                        isNeedUpdate = true;
                    }

                    if (isNeedUpdate || isManual)
                    {
                        DialogResult dialogResult = MessageBox.Show(returnMsg, "Обновление?", MessageBoxButtons.OKCancel);
                        if (dialogResult == DialogResult.OK && isNeedUpdate)
                        {
                            System.Diagnostics.Process.Start("https://github.com/Dark-V/SynthBorg/releases");
                            this.Close();
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                LogMessage($"Что-то пошло не так..! А именно: {ex.Message}");
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
                    case "/attach":
                        if (commandParts.Length > 0)
                        {
                            VK_F1 = ShowHotkeyInputDialog();

                            UnregisterHotKey(this.Handle, HOTKEY_ID);
                            RegisterHotKey(this.Handle, HOTKEY_ID, MOD_SHIFT, VK_F1);
                        }
                        break;
                    case "/update":
                            CheckUpdatesOnGit(true);
                        break;
                    case "/jeebot":
                        if (commandParts.Length > 1)
                        {
                            if (commandParts[1] == "set")
                            {
                                jeebot_token = string.Join(" ", commandParts.Skip(2));
                            }
                        }
                        break;
                    case "/me":
                        if (commandParts.Length > 2)
                        {
                            if (commandParts[1] == "allow")
                            {
                                string text = string.Join(" ", commandParts.Skip(2));
                                text = text.Trim('"'); // remove quotation marks
                                voice_allow_msg = text;
                            }
                            if (commandParts[1] == "deny")
                            {
                                string text = string.Join(" ", commandParts.Skip(2));
                                text = text.Trim('"'); // remove quotation marks
                                voice_not_allow_msg = text;
                            }

                            LogMessage($"voice_allow_msg is \"{voice_allow_msg}\" and voice_not_allow_msg is \"{voice_not_allow_msg}\"");
                        }
                        else
                        {
                            LogError("me command need 3 arguments");
                        }
                        break;
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
                                LogMessage($"Successful saved your data.");
                            }
                        break;

                    case "/ttv_info":
                        if (commandParts.Length > 0)
                        {
                            WebSocketInfo = !WebSocketInfo;
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

                    case "/blocklist":

                        if (commandParts.Length == 1)
                        {
                            LogError("blocklist command need type of usage - [add/del/show]");
                            return;
                        }

                        if (commandParts[1] == "add")
                        {
                            if (commandParts.Length < 3)
                            {
                                LogError("blocklist add command need nickname as argument. Nickname not exist.");
                                return;
                            }

                            string username = commandParts[2];
                            await AddIgnoredUserAsync(username);
                        }
                        if (commandParts[1] == "del")
                        {
                            if (commandParts.Length < 3)
                            {
                                LogError("blocklist del command need nickname as argument. Nickname not exist.");
                                return;
                            }

                            string username = commandParts[2];
                            await RemoveIgnoredUserAsync(username);
                        }
                        if (commandParts[1] == "show")
                        {
                            List<string> ignoredUsers = GetIgnoredUsers();
                            string ignoredUsersList = string.Join(Environment.NewLine, ignoredUsers);
                            LogTextBoxOnly("Current list of blocked users:" + Environment.NewLine + ignoredUsersList + Environment.NewLine);
                        }
                        break;
                    case "/whitelist":

                        if (commandParts.Length == 1)
                        {
                            LogError("Whitelist command need type of usage - [add/del/show]");
                            return;
                        }

                        if (commandParts[1] == "add")
                        {
                            if (commandParts.Length < 3)
                            {
                                LogError("Whitelist add command need nickname as argument. Nickname not exist.");
                                return;
                            }

                            string username = commandParts[2];
                            await AddWhitelistedUserAsync(username);
                        }
                        if (commandParts[1] == "del")
                        {
                            if (commandParts.Length < 3)
                            {
                                LogError("Whitelist del command need nickname as argument. Nickname not exist.");
                                return;
                            }

                            string username = commandParts[2];
                            await RemoveWhitelistedUserAsync(username);
                        }
                        if (commandParts[1] == "show")
                        {
                            List<string> whitelistedUsers = GetWhitelistedUsers();
                            string whitelistedUsersList = string.Join(Environment.NewLine, whitelistedUsers);
                            LogTextBoxOnly("Current list of whitelisted users:" + Environment.NewLine + whitelistedUsersList + Environment.NewLine);
                        }
                        break;

                    case "/help":
                        if (commandParts.Length > 0)
                        {
                            string helpText = "Доступные команды:" + Environment.NewLine +
                                "/attach - Изменить клавишу хоткея пропуска tts озвучек" + Environment.NewLine +
                                "/clear - Очистить окно вывода" + Environment.NewLine +
                                "/ttv_info - Включает или выключает подробный вывод TTV" + Environment.NewLine +
                                "/save - Сохранить текущие настройки конфигурации" + Environment.NewLine +
                                "/reload - Перезагрузить подключение к TTV" + Environment.NewLine +
                                "/say [сообщение] - Произнести указанное сообщение внутри приложения" + Environment.NewLine +
                                "/blocklist [add/del/show] [имя_пользователя] Добавить/удалить/показать список пользователь игнор списка" + Environment.NewLine +
                                "/whitelist [add/del/show] [имя_пользователя] - Добавить/удалить/показать список пользователь белого списка" + Environment.NewLine +
                                "/me [allow/deny] \"текст_сообщения\" - Изменить текст сообщения для команды !me" + Environment.NewLine +
                                "/jeebot [set] \"токен\" - установить токен jeebot tts" + Environment.NewLine +
                                "/update - Проверить наличие обновления SynthBorg'a.";

                            LogTextBoxOnly(helpText);
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
                    userId = userId.ToLower();
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
                    userId = userId.ToLower();  
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

        private async void buttonTokenGen_Click(object sender, EventArgs e)
        {
            string urlf = "https://id.twitch.tv/oauth2/authorize?response_type=token&client_id=livbg1uzwm54a5wjkbqaht5l60elv3&redirect_uri=http://localhost:3000&scope=chat:read+chat:edit";

            DialogResult result = MessageBox.Show("Вы точно хотите создать новый токен?", "Token generation", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

            if (result == DialogResult.OK)
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://localhost:3000/");
                listener.Start();

                Process.Start(new ProcessStartInfo(urlf) { UseShellExecute = true });

                await Task.Factory.StartNew(() =>
                {
                    while (listener.IsListening)
                    {
                        var context = listener.GetContext();
                        context.Response.ContentType = "text/html; charset=UTF-8";
                        if (context.Request.HttpMethod == "GET")
                        {
                            string responseString = @"
                        <HTML><BODY>
                        <script>
                            var hash = window.location.hash.substr(1);
                            var xhr = new XMLHttpRequest();
                            xhr.open('POST', '/', true);
                            xhr.setRequestHeader('Content-Type', 'application/x-www-form-urlencoded');
                            xhr.send(hash);
                            xhr.onload = function () {
                                if (xhr.status == 200) {
                                    document.body.innerHTML = 'Токен получен. Вы можете вернуться в программу и закрыть эту вкладку.';
                                }
                            }
                        </script>
                        </BODY></HTML>";
                            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                            context.Response.ContentLength64 = buffer.Length;
                            System.IO.Stream output = context.Response.OutputStream;
                            output.Write(buffer, 0, buffer.Length);
                            output.Close();
                        }
                        else if (context.Request.HttpMethod == "POST")
                        {
                            System.IO.Stream body = context.Request.InputStream;
                            System.Text.Encoding encoding = context.Request.ContentEncoding;
                            System.IO.StreamReader reader = new System.IO.StreamReader(body, encoding);
                            context.Response.ContentType = "text/html; charset=UTF-8";

                            string s = reader.ReadToEnd();
                            token = Regex.Match(s, @"access_token=([^&]*)").Groups[1].Value;
                            reader.Close();
                            body.Close();

                            if (!string.IsNullOrEmpty(token))
                            {
                                Invoke(new Action(() => tokenBox.Text = token));
                            }

                            string responseString = "<HTML><BODY>Токен получен. Вы можете вернуться в программу и закрыть эту вкладку.</BODY></HTML>";
                            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                            context.Response.ContentLength64 = buffer.Length;
                            System.IO.Stream output = context.Response.OutputStream;
                            output.Write(buffer, 0, buffer.Length);
                            output.Close();

                            listener.Stop();
                            SaveConfig();
                        }
                    }
                });
            }
        }

        private void groupBox4_Enter(object sender, EventArgs e)
        {

        }
        private void stopall_btnClick(object sender, EventArgs e)
        {
            LogMessage("Force caneceling all speking task's.");

            synthesizer.SpeakAsyncCancelAll();

            if (currentWaveOut != null)
            {
                currentWaveOut.Stop();
            }
        }

        private void trackBar1_Scroll(object sender, EventArgs e)
        {

        }

        private void volumeBar_Scroll(object sender, EventArgs e)
        {
            toolTip.RemoveAll();
            toolTip.Show(volumeBar.Value.ToString(), (System.Windows.Forms.TrackBar)sender);
        }
    }
}
