using NAudio.Dsp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Enregistreur_vocal
{
    internal class FiltreAudio
    {

        private readonly float a0, a1, a2, b1, b2;
        private float z1 = 0f, z2 = 0f;

        // Design d’un filtre passe‑haut ou passe‑bas
        public static FiltreAudio CreateLowPass(float sampleRate, float cutoffFreq)
        {
            var w0 = (float)(2 * Math.PI * cutoffFreq / sampleRate);
            var cosW = (float)Math.Cos(w0);
            var sinW = (float)Math.Sin(w0);
            var alpha = sinW / 2f; // Q = sqrt(2)/2

            float b0 = (1 - cosW) / 2;
            float b1_ = 1 - cosW;
            float b2_ = (1 - cosW) / 2;
            float a0_ = 1 + alpha;
            float a1_ = -2 * cosW;
            float a2_ = 1 - alpha;

            return new FiltreAudio(b0 / a0_, b1_ / a0_, b2_ / a0_, a1_ / a0_, a2_ / a0_);
        }

        public static FiltreAudio CreateHighPass(float sampleRate, float cutoffFreq)
        {
            var w0 = (float)(2 * Math.PI * cutoffFreq / sampleRate);
            var cosW = (float)Math.Cos(w0);
            var sinW = (float)Math.Sin(w0);
            var alpha = sinW / 2f; // Q = sqrt(2)/2

            float b0 = (1 + cosW) / 2;
            float b1_ = -(1 + cosW);
            float b2_ = (1 + cosW) / 2;
            float a0_ = 1 + alpha;
            float a1_ = -2 * cosW;
            float a2_ = 1 - alpha;

            return new FiltreAudio(b0 / a0_, b1_ / a0_, b2_ / a0_, a1_ / a0_, a2_ / a0_);
        }

        private FiltreAudio(float _b0, float _b1, float _b2, float _a1, float _a2)
        {
            b1 = _b1; b2 = _b2;
            a1 = _a1; a2 = _a2;
            a0 = 1f; // déjà normalisé
        }

        public short ProcessSample(short input)
        {
            float inF = input / 32768f;
            float outF = (float)(b1 * inF + z1);
            z1 = (float)(z2 - b2 * inF + a1 * outF);
            z2 = (float)(-a2 * outF);

            int outInt = (int)Math.Max(-32768, Math.Min(32767, outF * 32768));
            return (short)outInt;
        }


    }
}
