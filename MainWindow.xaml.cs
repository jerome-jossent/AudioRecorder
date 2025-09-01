using NAudio.Dsp;
using NAudio.Lame;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

using System.Diagnostics;

namespace Enregistreur_vocal
{
    public partial class MainWindow : Window
    {
        // Capture audio
        WaveInEvent waveIn;
        WaveFileWriter writer;

        // Lecture audio
        IWavePlayer waveOut;
        AudioFileReader audioReader;

        // Chemin du fichier d’enregistrement
        string recordingPath;

        // filtres 
        private FiltreAudio lowPass = null;   // ~16 kHz
        private FiltreAudio highPass = null; // ~80 Hz

        public MainWindow()
        {
            InitializeComponent();
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
        //void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        //{
        //    // Écriture dans le fichier
        //    if (writer != null)
        //        writer.Write(e.Buffer, 0, e.BytesRecorded);

        //    // ----- Calcul de l’intensité -----
        //    float sumSquares = 0;
        //    int bytesPerSample = waveIn.WaveFormat.BitsPerSample / 8; // 2 pour 16 bits
        //    for (int index = 0; index < e.BytesRecorded; index += bytesPerSample)
        //    {
        //        short sample = BitConverter.ToInt16(e.Buffer, index);

        //        // Filtrage
        //        if (highPass != null) sample = highPass.ProcessSample(sample);
        //        if (lowPass != null) sample = lowPass.ProcessSample(sample);

        //        // Remplacer le buffer filtré
        //        Array.Copy(BitConverter.GetBytes(sample), 0, e.Buffer, index, bytesPerSample);

        //        float normalized = sample / 32768f;          // [-1.0 , +1.0]
        //        sumSquares += normalized * normalized;
        //    }

        //    float rms = (float)Math.Sqrt(sumSquares / (e.BytesRecorded / bytesPerSample));
        //    float db = 20 * (float)Math.Log10(rms > 0 ? rms : 0.00001f); // décibels
        //    int percent = (int)(Math.Max(0, Math.Min(100, ((db + 60) / 60) * 100)));

        //    // Mettre à jour la ProgressBar sur le thread UI
        //    Dispatcher.Invoke(() => LevelBar.Value = percent);
        //}


        private void WaveIn_RecordingStopped(object sender, StoppedEventArgs e)
        {
            writer?.Dispose();
            writer = null;
            waveIn.Dispose();
            waveIn = null;

            ConvertWaveToMP3();

            Dispatcher.Invoke(() =>
            {
                LevelBar.Value = 0;
                StatusText.Text = $"Audio Record completed – {recordingPath} - Click to access file";
                StatusText.ToolTip = StatusText.Text;
            });

            WindowsExplorer_OpenAndSelect.OpenAndSelect(recordingPath);
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
                MessageBox.Show($"Erreur de conversion : {ex.Message}");
            }
        }

        private void BtnRefreshInput_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            PopulateDevices();
        }

        private void PopulateDevices()
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

            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "Play end.";

                if (_ckb_Buzz.IsChecked == true)
                    StartBuzz();
            });
        }

        /* ---------- Boutons ---------- */
        private void BtnRecord_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (waveIn != null) return; // déjà en train d’enregistrer

            recordingPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Vocal " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".wav");

            InitCapture();
            waveIn.StartRecording();

            StatusText.Text = "Recording…";
        }

        private void BtnStopRec_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (waveIn == null) return;
            waveIn.StopRecording();
        }

        private void BtnPlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!File.Exists(recordingPath)) return;


            if (waveOut != null) return; // déjà en lecture

            InitPlayback();
            waveOut.Play();

            StatusText.Text = "Playing…";
        }

        private void OpenFolder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
                //si buzz est ouvert, le fermer !




                string srtContent = await TranscrireAsync(recordingPath);

                // Afficher le SRT dans un TextBox ou l’enregistrer ailleurs
                _tbx_transcription.Text = srtContent;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task<string> TranscrireAsync(string mp3FilePath)
        {
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

            // Charger le fichier SRT produit
            string srtPath = Path.ChangeExtension(mp3FilePath, ".srt");
            return File.ReadAllText(srtPath);
        }

    }
}