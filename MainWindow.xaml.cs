using NAudio.Lame;
using NAudio.Wave;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Enregistreur_vocal
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region VARIABLES
        // Capture audio
        WaveInEvent waveIn;
        WaveFileWriter writer;

        // Filtres audio
        FiltreAudio lowPass = null;   // ~16 kHz
        FiltreAudio highPass = null; // ~80 Hz

        object access_to_audioWAVfile = new object();

        // filewatcher pour la production du fichier SRT de la transcription par BuZZ
        FileSystemWatcher transcription_watcher;

        #region BINDING
        public bool audio_recording
        {
            get => _audio_recording; set
            {
                if (_audio_recording == value) return;
                _audio_recording = value;
                NotifyPropertyChanged();
            }
        }
        bool _audio_recording;

        public string recordingFolder
        {
            get => _recordingFolder; set
            {
                if (_recordingFolder == value) return;
                _recordingFolder = value;
                NotifyPropertyChanged();
            }
        }
        string _recordingFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

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

        enum FileType { audioWAV, audioMP3, transcriptionTXT, resultTXT }

        Dictionary<FileType, string> extensions = new Dictionary<FileType, string>() {
            {FileType.audioWAV, ".wav" },
            {FileType.audioMP3, ".mp3" },
            {FileType.transcriptionTXT, ".transcription.txt" },
            {FileType.resultTXT, ".txt" },
        };

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

        public int audio_amplitude
        {
            get => _audio_amplitude; set
            {
                if (_audio_amplitude == value) return;
                _audio_amplitude = value;
                NotifyPropertyChanged();
            }
        }
        int _audio_amplitude;

        #endregion

        public enum TypePrompt { demande, resume }
        public TypePrompt llm_typePrompt = TypePrompt.demande;

        #endregion

        Dictionary<TypePrompt, string> prompts;

        void LLM_prompts_init()
        {
            prompts = new Dictionary<TypePrompt, string>();
            prompts.Add(TypePrompt.demande, "Utilise la retranscription suivante comme une demande :");
            prompts.Add(TypePrompt.resume, "Peux tu me faire un résumé de la retranscription suivante :");
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        void IHM_Loaded(object sender, RoutedEventArgs e)
        {
            Audio_PopulateDevices();
            LLM_prompts_init();
        }

        void IHM_Closing(object sender, CancelEventArgs e)
        {
            Audio_ListeningSTOP();
        }

        /* ---------- Capture audio ---------- */
        void BtnRefreshInput_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { Audio_PopulateDevices(); }

        void Audio_PopulateDevices()
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
                DeviceSelector.SelectedIndex = 0;  //déclenche DeviceSelector_SelectionChanged()           
        }

        void DeviceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Audio_ListeningSTOP();
            Audio_InitCapture();
        }

        void Audio_ListeningSTOP()
        {
            if (waveIn == null) return;
            waveIn.StopRecording();
        }

        void Audio_InitCapture()
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

            waveIn.DataAvailable += Audio_WaveIn_DataAvailable;
            waveIn.RecordingStopped += Audio_WaveIn_RecordingStopped;

            waveIn.StartRecording();
        }

        void Audio_WaveIn_DataAvailable(object sender, WaveInEventArgs e)
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
            if (audio_recording)
            {
                if (writer == null)
                    writer = new WaveFileWriter(recordingPath + extensions[FileType.audioWAV], waveIn.WaveFormat);

                lock (access_to_audioWAVfile)
                {
                    writer.Write(filteredBuffer, 0, filteredBuffer.Length);
                }
            }

            // ----------- Calcul de la barre de niveau ----------
            float rms = (float)Math.Sqrt(sumSquares / (e.BytesRecorded / bytesPerSample));
            float db = 20 * (float)Math.Log10(rms > 0 ? rms : 0.00001f);
            int percent = (int)(Math.Max(0, Math.Min(100, ((db + 60) / 60) * 100)));

            audio_amplitude = percent;
        }

        void Audio_WaveIn_RecordingStopped(object sender, StoppedEventArgs e)
        {
            waveIn.Dispose();
            waveIn = null;
        }

        void OnNewWav()
        {
            Update_StatusBar($"Converting WAV to MP3…");
            lock (access_to_audioWAVfile)
            {
                writer?.Dispose();
                writer = null;
            }
            Audio_ConvertWaveToMP3();
            File.Delete(recordingPath + extensions[FileType.audioWAV]);
            Update_StatusBar($"Record completed – {recordingPath}");

            if (_ckb_Buzz.IsChecked == true)
                StartBuzz();
        }

        void Audio_ConvertWaveToMP3()
        {
            string wavPath = recordingPath + extensions[FileType.audioWAV];
            string mp3Path = recordingPath + extensions[FileType.audioMP3];

            try
            {
                using (AudioFileReader reader = new AudioFileReader(wavPath))
                {
                    // 128 kbps, mono ou stereo selon le fichier source
                    LameMP3FileWriter encoder = new LameMP3FileWriter(mp3Path, reader.WaveFormat, 128);
                    reader.CopyTo(encoder);   // lit l’AudioFileReader et écrit dans MP3
                    encoder.Flush();          // important !
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Erreur de conversion : {ex.Message}");
            }
        }



        /* ---------- Create Audio ---------- */
        void BtnRecord_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            audio_recording = true;
            Update_AudioINState();
            RecordingPath();
        }
        void BtnStopRec_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            audio_recording = false;
            Update_AudioINState();
            OnNewWav();
        }


        void Update_AudioINState()
        {
            Update_StatusBar(audio_recording ? "Recording…" : "Listening…");
        }

        void BtnFolder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            string file = recordingPath + extensions[FileType.audioWAV];
            if (!File.Exists(file))
                file = recordingPath + extensions[FileType.audioMP3];

            JJO_Tools.WindowsExplorer_OpenAndSelect.OpenAndSelect(file);
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

                await TranscrireAsync(recordingPath + extensions[FileType.audioMP3]);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        async Task TranscrireAsync(string filePath)
        {
            Update_StatusBar("BuZZ Starting…");
            var args = $@"add --task transcribe --model-type whisper --model-size medium -l fr --srt ""{filePath}""";

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

            transcription_watcher = new FileSystemWatcher(folderToWatch);
            // On ne regarde pas les sous‑dossiers
            transcription_watcher.IncludeSubdirectories = false;
            // Seul le filtre sur l’extension .srt est activé
            transcription_watcher.Filter = "*.srt";
            // On s’abonne uniquement à l’événement Created (nouveau fichier)
            transcription_watcher.Created += SRT_Created;
            // Démarrage de la surveillance
            transcription_watcher.EnableRaisingEvents = true;
        }

        void FileWatcher_STOP()
        {
            transcription_watcher.Created -= SRT_Created;
            transcription_watcher.EnableRaisingEvents = false;
            transcription_watcher.Dispose();
            transcription_watcher = null;
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
                Dispatcher.Invoke(() => { _tbx_transcription.Text = srtContent; });

                LLM_AskAndGetResponse(srtContent, LLM_Studio_Model);
            }
        }

        void LLM_Send_Manually(object sender, RoutedEventArgs e)
        {
            string txt = _tbx_transcription.Text;
            LLM_AskAndGetResponse(txt, LLM_Studio_Model);
        }

        async Task LLM_AskAndGetResponse(string txt, string llmStudio_Model)
        {
            Update_StatusBar("Prompt sended to LLM…");
            LLM_WriteResponseToHMI("");

            string UserPrompt = prompts[llm_typePrompt] + "\n\n" + txt;

            var client = new LMStudio_Client();
            string reponse = await client.GetChatCompletionAsync(UserPrompt, LLM_Studio_Model);

            LLM_WriteResponseToHMI(reponse);
            LLM_SaveReponse(reponse, ".resume");
        }

        void LLM_WriteResponseToHMI(string response)
        {
            // 1. Créez un FlowDocument vide (ou récupérez-en un existant)
            FlowDocument doc = new FlowDocument();

            // 2. Ajoutez un paragraphe avec du texte brut
            Paragraph p1 = new Paragraph(new Run(response));
            doc.Blocks.Add(p1);

            //// 3. Ajoutez un paragraphe formaté (gras, couleur)
            //Run formattedRun = new Run("Texte en gras et rouge.")
            //{
            //    FontWeight = FontWeights.Bold,
            //    Foreground = Brushes.Red
            //};
            //Paragraph p2 = new Paragraph(formattedRun);
            //doc.Blocks.Add(p2);

            //// 4. Ajoutez un paragraphe avec une liste
            //List list = new List();
            //list.ListItems.Add(new ListItem(new Paragraph(new Run("Élément 1"))));
            //list.ListItems.Add(new ListItem(new Paragraph(new Run("Élément 2"))));
            //doc.Blocks.Add(list);

            // 5. Affectez le FlowDocument au RichTextBox

            Dispatcher.Invoke(() => { _tbx_reponseLLM.Document = doc; });

        }

        void LLM_SaveReponse(string reponse, string suffixe = "")
        {
            RecordingPath();
            string path = recordingPath + suffixe + extensions[FileType.resultTXT]; ;
            File.WriteAllText(path, reponse);
        }

        void RecordingPath(string fileName = null)
        {
            if (fileName == null)
            {
                recordingPath = Path.Combine(recordingFolder, "Vocal " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
            }
            else
            {
                FileInfo fileInfo = new FileInfo(fileName);
                //remove extension
                string ext = fileInfo.Extension;
                recordingPath = fileInfo.FullName.Replace(ext, "");
            }
        }

        private void Debug_PickWave_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
            ofd.Filter = "Audio File|*.wav;*.mp3";
            if (ofd.ShowDialog() != true) return;
            RecordingPath(ofd.FileName);
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