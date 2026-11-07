using System;
using UnityEngine;

namespace ThinkingSpace.Speech
{
    public readonly struct WavValidationResult
    {
        public readonly bool IsValid;
        public readonly bool MetadataMatchesRecording;
        public readonly int SampleRate;
        public readonly int Channels;
        public readonly int BitsPerSample;
        public readonly int DataBytes;
        public readonly int SampleFrames;
        public readonly int NonzeroSampleValues;
        public readonly float DurationSeconds;
        public readonly float Rms;
        public readonly float Peak;
        public readonly string Error;

        public WavValidationResult(bool isValid, bool metadataMatchesRecording, int sampleRate,
            int channels, int bitsPerSample, int dataBytes, int sampleFrames, int nonzeroSampleValues,
            float durationSeconds, float rms, float peak, string error)
        {
            IsValid = isValid;
            MetadataMatchesRecording = metadataMatchesRecording;
            SampleRate = sampleRate;
            Channels = channels;
            BitsPerSample = bitsPerSample;
            DataBytes = dataBytes;
            SampleFrames = sampleFrames;
            NonzeroSampleValues = nonzeroSampleValues;
            DurationSeconds = durationSeconds;
            Rms = rms;
            Peak = peak;
            Error = error;
        }

        public string ToDiagnosticString()
        {
            return "WAV valid: " + IsValid + "   Metadata match: " + MetadataMatchesRecording
                + "\nPCM: " + SampleRate + " Hz   " + Channels + " ch   " + BitsPerSample + " bit"
                + "\nData: " + DataBytes + " bytes   Frames: " + SampleFrames
                + "   Duration: " + DurationSeconds.ToString("0.000") + "s"
                + "\nSignal RMS: " + Rms.ToString("0.0000") + "   Peak: " + Peak.ToString("0.0000")
                + "   Nonzero values: " + NonzeroSampleValues
                + (string.IsNullOrEmpty(Error) ? string.Empty : "\nValidation error: " + Error);
        }
    }

    public static class WavEncoder
    {
        private const int HeaderSize = 44;
        private const int FramesPerChunk = 8192;

        public static byte[] Encode16BitPcm(CompletedMicrophoneRecording recording)
        {
            if (recording.Clip == null) throw new ArgumentException("Recording has no AudioClip.");
            if (recording.SampleFrames <= 0 || recording.SampleRate <= 0 || recording.Channels <= 0)
                throw new ArgumentException("Recording metadata is invalid or empty.");
            if (recording.SampleFrames > recording.Clip.samples || recording.Channels != recording.Clip.channels
                || recording.SampleRate != recording.Clip.frequency)
                throw new ArgumentException("Recording metadata does not match its AudioClip.");

            long sampleValueCount = (long)recording.SampleFrames * recording.Channels;
            long dataByteCount = sampleValueCount * 2L;
            if (dataByteCount > int.MaxValue - HeaderSize)
                throw new ArgumentException("Recording is too large to encode in memory.");

            byte[] wav = new byte[HeaderSize + (int)dataByteCount];
            WriteAscii(wav, 0, "RIFF");
            WriteInt32(wav, 4, wav.Length - 8);
            WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt ");
            WriteInt32(wav, 16, 16);
            WriteInt16(wav, 20, 1);
            WriteInt16(wav, 22, recording.Channels);
            WriteInt32(wav, 24, recording.SampleRate);
            WriteInt32(wav, 28, recording.SampleRate * recording.Channels * 2);
            WriteInt16(wav, 32, recording.Channels * 2);
            WriteInt16(wav, 34, 16);
            WriteAscii(wav, 36, "data");
            WriteInt32(wav, 40, (int)dataByteCount);

            int chunkFrames = Mathf.Min(FramesPerChunk, recording.SampleFrames);
            float[] samples = new float[chunkFrames * recording.Channels];
            int outputOffset = HeaderSize;
            for (int frameOffset = 0; frameOffset < recording.SampleFrames; frameOffset += chunkFrames)
            {
                int framesThisChunk = Mathf.Min(chunkFrames, recording.SampleFrames - frameOffset);
                if (!recording.Clip.GetData(samples, frameOffset))
                    throw new InvalidOperationException("Could not read PCM samples from the completed AudioClip.");
                int valuesThisChunk = framesThisChunk * recording.Channels;
                for (int i = 0; i < valuesThisChunk; i++)
                {
                    float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                    short pcm = clamped < 0f
                        ? (short)Mathf.RoundToInt(clamped * 32768f)
                        : (short)Mathf.RoundToInt(clamped * 32767f);
                    wav[outputOffset++] = (byte)(pcm & 0xff);
                    wav[outputOffset++] = (byte)((pcm >> 8) & 0xff);
                }
            }
            return wav;
        }

        public static WavValidationResult Validate16BitPcm(byte[] wav, CompletedMicrophoneRecording recording)
        {
            if (wav == null || wav.Length < HeaderSize)
                return Invalid("WAV is missing or shorter than its 44-byte header.");
            if (!MatchesAscii(wav, 0, "RIFF") || !MatchesAscii(wav, 8, "WAVE")
                || !MatchesAscii(wav, 12, "fmt ") || !MatchesAscii(wav, 36, "data"))
                return Invalid("RIFF/WAVE/fmt/data markers are invalid.");

            int riffSize = ReadInt32(wav, 4);
            int formatChunkSize = ReadInt32(wav, 16);
            int audioFormat = ReadInt16(wav, 20);
            int channels = ReadInt16(wav, 22);
            int sampleRate = ReadInt32(wav, 24);
            int byteRate = ReadInt32(wav, 28);
            int blockAlign = ReadInt16(wav, 32);
            int bitsPerSample = ReadInt16(wav, 34);
            int dataBytes = ReadInt32(wav, 40);
            string error = string.Empty;
            if (riffSize != wav.Length - 8) error = "RIFF size does not match the file length.";
            else if (formatChunkSize != 16 || audioFormat != 1) error = "WAV is not standard PCM format.";
            else if (channels <= 0 || sampleRate <= 0 || bitsPerSample != 16) error = "PCM format fields are invalid.";
            else if (blockAlign != channels * 2 || byteRate != sampleRate * blockAlign) error = "PCM byte rate or block alignment is invalid.";
            else if (dataBytes < 0 || HeaderSize + dataBytes != wav.Length || dataBytes % blockAlign != 0)
                error = "PCM data size does not match the file length or frame alignment.";
            if (!string.IsNullOrEmpty(error)) return Invalid(error);

            int frames = dataBytes / blockAlign;
            int valueCount = dataBytes / 2;
            int nonzero = 0;
            double sum = 0d;
            float peak = 0f;
            for (int offset = HeaderSize; offset < HeaderSize + dataBytes; offset += 2)
            {
                short value = (short)(wav[offset] | (wav[offset + 1] << 8));
                if (value != 0) nonzero++;
                float sample = value < 0 ? value / 32768f : value / 32767f;
                float absolute = Mathf.Abs(sample);
                if (absolute > peak) peak = absolute;
                sum += sample * sample;
            }
            float rms = valueCount > 0 ? Mathf.Sqrt((float)(sum / valueCount)) : 0f;
            float duration = frames / (float)sampleRate;
            bool metadataMatches = recording.Clip != null
                && recording.SampleFrames == frames
                && recording.SampleRate == sampleRate
                && recording.Channels == channels
                && recording.Clip.samples == frames
                && recording.Clip.frequency == sampleRate
                && recording.Clip.channels == channels
                && Mathf.Abs(recording.DurationSeconds - duration) <= 1f / sampleRate
                && Mathf.Abs(recording.Rms - rms) <= 0.0001f
                && Mathf.Abs(recording.Peak - peak) <= 0.0001f;
            return new WavValidationResult(true, metadataMatches, sampleRate, channels, bitsPerSample,
                dataBytes, frames, nonzero, duration, rms, peak,
                metadataMatches ? string.Empty : "Encoded WAV metadata or signal statistics differ from the retained recording.");
        }

        private static WavValidationResult Invalid(string error)
        {
            return new WavValidationResult(false, false, 0, 0, 0, 0, 0, 0, 0f, 0f, 0f, error);
        }

        private static bool MatchesAscii(byte[] source, int offset, string value)
        {
            if (source.Length < offset + value.Length) return false;
            for (int i = 0; i < value.Length; i++)
                if (source[offset + i] != (byte)value[i]) return false;
            return true;
        }

        private static int ReadInt16(byte[] source, int offset)
        {
            return source[offset] | (source[offset + 1] << 8);
        }

        private static int ReadInt32(byte[] source, int offset)
        {
            return source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16)
                | (source[offset + 3] << 24);
        }

        private static void WriteAscii(byte[] target, int offset, string value)
        {
            for (int i = 0; i < value.Length; i++) target[offset + i] = (byte)value[i];
        }

        private static void WriteInt16(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value & 0xff);
            target[offset + 1] = (byte)((value >> 8) & 0xff);
        }

        private static void WriteInt32(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value & 0xff);
            target[offset + 1] = (byte)((value >> 8) & 0xff);
            target[offset + 2] = (byte)((value >> 16) & 0xff);
            target[offset + 3] = (byte)((value >> 24) & 0xff);
        }
    }
}
