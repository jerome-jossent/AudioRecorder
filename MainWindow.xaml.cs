using NAudio.Lame;
using NAudio.Wave;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Enregistreur_vocal
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Capture audio
        WaveInEvent waveIn;
        WaveFileWriter writer;

        // Filtres audio
        FiltreAudio lowPass = null;   // ~16 kHz
        FiltreAudio highPass = null; // ~80 Hz

        // Lecture audio
        IWavePlayer waveOut;
        AudioFileReader audioReader;

        // filewatcher pour la production du fichier SRT de la transcription par BuZZ
        FileSystemWatcher watcher;

        #region BINDING
        // Chemin du fichier d’enregistrement
        public string recordingPath
        {
            get => _recordingPath; set
            {
                if (_recordingPath == value) return;
                _recordingPath = value;
                NotifyPropertyChanged();
            }
        }
        string _recordingPath;

        public string LLM_Studio_Model
        {
            get => _LLM_Studio_Model; set
            {
                if (_LLM_Studio_Model == value) return;
                _LLM_Studio_Model = value;
                NotifyPropertyChanged();
            }
        }
        string _LLM_Studio_Model = "openai/gpt-oss-20b";
        #endregion


        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            PopulateDevices();
        }

        /* ---------- Capture audio ---------- */
        void InitCapture()
        {
            int deviceIndex = (int)((ComboBoxItem)DeviceSelector.SelectedItem).Tag;

            waveIn = new WaveInEvent
            {
                DeviceNumber = deviceIndex,                 // sélection du menu déroulant
                WaveFormat = new WaveFormat(44100, 16, 1),
                BufferMilliseconds = 100,
                NumberOfBuffers = 3
            };

            float sampleRate = waveIn.WaveFormat.SampleRate;
            lowPass = FiltreAudio.CreateLowPass(sampleRate, 15000f);   // passe‑bas
            highPass = FiltreAudio.CreateHighPass(sampleRate, 60f);    // passe‑haut

            waveIn.DataAvailable += WaveIn_DataAvailable;
            waveIn.RecordingStopped += WaveIn_RecordingStopped;

            writer = new WaveFileWriter(recordingPath, waveIn.WaveFormat);
        }


        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            int bytesPerSample = waveIn.WaveFormat.BitsPerSample / 8;   // 2 pour 16‑bits
            int channels = waveIn.WaveFormat.Channels;           // 1 ou 2

            // Buffer où nous stockerons les échantillons filtrés (même taille que e.Buffer)
            byte[] filteredBuffer = new byte[e.BytesRecorded];

            float sumSquares = 0f;

            for (int i = 0; i < e.BytesRecorded; i += bytesPerSample * channels)
            {
                // ---------- Traitement de chaque canal ----------
                for (int ch = 0; ch < channels; ch++)
                {
                    int offset = i + ch * bytesPerSample;

                    short sample = BitConverter.ToInt16(e.Buffer, offset);

                    // Filtre
                    if (highPass != null) sample = highPass.ProcessSample(sample);
                    if (lowPass != null) sample = lowPass.ProcessSample(sample);

                    // Stocker le résultat filtré
                    Array.Copy(BitConverter.GetBytes(sample), 0,
                               filteredBuffer, offset, bytesPerSample);

                    // Pour la barre de niveau, on peut choisir d’utiliser la moyenne des canaux
                    float normalized = sample / 32768f;          // [-1.0 , +1.0]
                    sumSquares += normalized * normalized;
                }
            }

            // ----------- Écriture du fichier ----------
            if (writer != null)
                writer.Write(filteredBuffer, 0, filteredBuffer.Length);

            // ----------- Calcul de la barre de niveau ----------
            float rms = (float)Math.Sqrt(sumSquares / (e.BytesRecorded / bytesPerSample));
            float db = 20 * (float)Math.Log10(rms > 0 ? rms : 0.00001f);
            int percent = (int)(Math.Max(0, Math.Min(100, ((db + 60) / 60) * 100)));

            Dispatcher.Invoke(() => LevelBar.Value = percent);
        }


        private void WaveIn_RecordingStopped(object sender, StoppedEventArgs e)
        {
            writer?.Dispose();
            writer = null;
            waveIn.Dispose();
            waveIn = null;

            ConvertWaveToMP3();

            Update_StatusBar($"Record completed – {recordingPath}");

            Dispatcher.Invoke(() =>
            {
                LevelBar.Value = 0;
            });

            if (_ckb_Buzz.IsChecked == true)
                StartBuzz();
        }

        void ConvertWaveToMP3()
        {
            string wavPath = recordingPath;
            string mp3Path = Path.ChangeExtension(wavPath, ".mp3");

            try
            {
                using (AudioFileReader reader = new AudioFileReader(wavPath))
                {
                    // 128 kbps, mono ou stereo selon le fichier source
                    LameMP3FileWriter encoder = new LameMP3FileWriter(mp3Path, reader.WaveFormat, 128);
                    reader.CopyTo(encoder);   // lit l’AudioFileReader et écrit dans MP3
                    encoder.Flush();          // important !
                }
                recordingPath = mp3Path;
                File.Delete(wavPath);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Erreur de conversion : {ex.Message}");
            }
        }

        void BtnRefreshInput_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { PopulateDevices(); }

        void PopulateDevices()
        {
            DeviceSelector.Items.Clear();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                WaveInCapabilities caps = WaveIn.GetCapabilities(i);
                DeviceSelector.Items.Add(new ComboBoxItem
                {
                    Content = $"{i} – {caps.ProductName}",
                    Tag = i          // stocke l’index du périphérique
                });
            }

            // Sélectionne le premier par défaut
            if (DeviceSelector.Items.Count > 0)
                DeviceSelector.SelectedIndex = 0;
        }

        /* ---------- Lecture audio ---------- */
        private void InitPlayback()
        {
            audioReader = new AudioFileReader(recordingPath);
            waveOut = new WaveOutEvent();
            waveOut.Init(audioReader);
            waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
        }

        private void WaveOut_PlaybackStopped(object sender, StoppedEventArgs e)
        {
            waveOut.Dispose();
            audioReader.Dispose();
            waveOut = null;
            audioReader = null;

            Update_StatusBar("Play end.");
        }

        /* ---------- Boutons ---------- */
        void BtnRecord_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (waveIn != null) return; // déjà en train d’enregistrer

            recordingPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Vocal " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".wav");

            InitCapture();
            waveIn.StartRecording();

            Update_StatusBar("Recording…");
        }

        void BtnStopRec_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (waveIn == null) return;
            waveIn.StopRecording();
        }

        void BtnPlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!File.Exists(recordingPath)) return;
            if (waveOut != null) return; // déjà en lecture

            InitPlayback();
            waveOut.Play();

            Update_StatusBar("Playing…");
        }

        void BtnFolder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            WindowsExplorer_OpenAndSelect.OpenAndSelect(recordingPath);
        }

        void BtnBuzz_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            StartBuzz();
        }

        async void StartBuzz()
        {
            try
            {
                //si buzz est ouvert, le fermer ?!


                FileWatchSRT(recordingPath);

                await TranscrireAsync(recordingPath);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        async Task TranscrireAsync(string mp3FilePath)
        {
            Update_StatusBar("BuZZ Starting…");
            var args = $@"add --task transcribe --model-type whisper --model-size medium -l fr --srt ""{mp3FilePath}""";

            var startInfo = new ProcessStartInfo
            {
                FileName = @"C:\Program Files (x86)\Buzz\buzz.exe",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) throw new InvalidOperationException("Impossible de démarrer buzz.");

            string stdOut = await process.StandardOutput.ReadToEndAsync();
            string stdErr = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new Exception($"buzz a échoué ({process.ExitCode}): {stdErr}");
        }

        void Update_StatusBar(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
                StatusText.ToolTip = message;
            });
        }

        void FileWatchSRT(string filePath)
        {
            string folderToWatch = Path.GetDirectoryName(filePath);

            watcher = new FileSystemWatcher(folderToWatch);
            // On ne regarde pas les sous‑dossiers
            watcher.IncludeSubdirectories = false;
            // Seul le filtre sur l’extension .srt est activé
            watcher.Filter = "*.srt";
            // On s’abonne uniquement à l’événement Created (nouveau fichier)
            watcher.Created += SRT_Created;
            // Démarrage de la surveillance
            watcher.EnableRaisingEvents = true;
        }

        void FileWatcher_STOP()
        {
            watcher.Created -= SRT_Created;
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            watcher = null;
        }

        void SRT_Created(object sender, FileSystemEventArgs e)
        {
            // On vérifie que le fichier est bien un .srt (juste au cas où)
            if (Path.GetExtension(e.FullPath).Equals(".srt", StringComparison.OrdinalIgnoreCase))
            {
                FileWatcher_STOP();

                Update_StatusBar("Transcription ended");
                string srtContent = File.ReadAllText(e.FullPath);
                // Afficher le SRT

                Dispatcher.Invoke(() =>
                {
                    _tbx_transcription.Text = srtContent;
                });

                Send_to_LLM_ClickAsync(srtContent, LLM_Studio_Model);
            }
        }

        void Send_to_LLM_Click(object sender, RoutedEventArgs e)
        {
            string txt = _tbx_transcription.Text;
            Send_to_LLM_ClickAsync(txt, LLM_Studio_Model);
        }


        async Task Send_to_LLM_ClickAsync(string txt, string llmStudio_Model)
        {
            Update_StatusBar("Prompt sended to LLM…");
            Dispatcher.Invoke(() => { _tbx_reponseLLM.Text = ""; });

            string UserPrompt = "Peux tu me faire un résumé de la retranscription suivante :\n\n" + txt;
            var client = new LMStudio_Client();
            string reponse = await client.GetChatCompletionAsync(UserPrompt, LLM_Studio_Model);

            Dispatcher.Invoke(() => { _tbx_reponseLLM.Text = reponse; });
        }

        private void Debug_PickWave_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
            ofd.Filter = "Audio File|*.wav;*.mp3";
            if (ofd.ShowDialog() != true) return;
            recordingPath = ofd.FileName;
            StartBuzz();
        }

        void TextBox_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            // Vérifier si l'élément glissé contient des fichiers
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                e.Effects = System.Windows.DragDropEffects.Copy;
            else
                e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        void TextBox_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string filePath = files[0];
                    _tbx_transcription.Text = File.ReadAllText(filePath);
                }
            }
        }
    }
}