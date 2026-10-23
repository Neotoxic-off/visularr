import type { DeviceProfile } from "@/lib/api";

type Codec = [name: string, mime: string];

// Probes in order of preference: the first transcoding codec is what the server encodes to when it cannot copy.
const VIDEO: Codec[] = [
  ["h264", 'video/mp4; codecs="avc1.640029"'],
  ["hevc", 'video/mp4; codecs="hvc1.1.6.L120.90"'],
  ["hevc", 'video/mp4; codecs="hev1.1.6.L120.90"'],
  ["av1", 'video/mp4; codecs="av01.0.08M.08"'],
  ["vp9", 'video/mp4; codecs="vp09.00.40.08"'],
];
const AUDIO: Codec[] = [
  ["aac", 'audio/mp4; codecs="mp4a.40.2"'],
  ["mp3", "audio/mpeg"],
  ["ac3", 'audio/mp4; codecs="ac-3"'],
  ["eac3", 'audio/mp4; codecs="ec-3"'],
  ["opus", 'audio/mp4; codecs="opus"'],
  ["flac", 'audio/mp4; codecs="flac"'],
];

// What this browser plays natively; the server remuxes or transcodes the rest to fMP4 HLS, copying any stream listed here.
export function deviceProfile(): DeviceProfile {
  const probe: HTMLVideoElement = document.createElement("video");
  // canPlayType covers native playback (Safari's own HLS), MSE covers what hls.js can feed.
  const can = (mime: string): boolean =>
    probe.canPlayType(mime) !== "" || (typeof MediaSource !== "undefined" && MediaSource.isTypeSupported(mime));
  const pick = (codecs: Codec[]): string[] => [...new Set(codecs.filter(([, mime]: Codec) => can(mime)).map(([name]: Codec) => name))];
  const video: string[] = pick(VIDEO);
  const audio: string[] = pick(AUDIO);
  if (!video.includes("h264")) video.unshift("h264");
  if (!audio.includes("aac")) audio.unshift("aac");
  const webmVideo: string[] = pick([["vp8", 'video/webm; codecs="vp8"'], ["vp9", 'video/webm; codecs="vp9"'], ["av1", 'video/webm; codecs="av01.0.08M.08"']]);
  const webmAudio: string[] = pick([["vorbis", 'audio/webm; codecs="vorbis"'], ["opus", 'audio/webm; codecs="opus"']]);
  // Chromium demuxes Matroska, yet most builds still answer "" for its MIME type.
  const brands: { brand: string }[] = (navigator as Navigator & { userAgentData?: { brands: { brand: string }[] } }).userAgentData?.brands ?? [];
  const mkv: boolean = probe.canPlayType("video/x-matroska") !== "" || brands.some((b: { brand: string }) => b.brand === "Chromium");
  // Dolby audio passes MSE checks on some platforms but not the Matroska demuxer.
  const mkvAudio: string[] = [...audio.filter((a: string) => a !== "ac3" && a !== "eac3"), ...webmAudio.filter((a: string) => a === "vorbis")];

  return {
    MaxStreamingBitrate: 120_000_000,
    MaxStaticBitrate: 100_000_000,
    MusicStreamingTranscodingBitrate: 384_000,
    DirectPlayProfiles: [
      { Container: "mp4,m4v,mov", Type: "Video", VideoCodec: video.join(","), AudioCodec: audio.join(",") },
      ...(webmVideo.length ? [{ Container: "webm", Type: "Video" as const, VideoCodec: webmVideo.join(","), AudioCodec: webmAudio.join(",") }] : []),
      ...(mkv ? [{ Container: "mkv", Type: "Video" as const, VideoCodec: video.join(","), AudioCodec: mkvAudio.join(",") }] : []),
    ],
    TranscodingProfiles: [
      { Container: "mp4", Type: "Video", VideoCodec: video.join(","), AudioCodec: audio.join(","), Context: "Streaming", Protocol: "hls", MaxAudioChannels: "6", MinSegments: 1, BreakOnNonKeyFrames: true },
    ],
    ContainerProfiles: [],
    CodecProfiles: [],
    // Text subtitles arrive as WebVTT side files; picture subtitles are burned in.
    SubtitleProfiles: [
      { Format: "vtt", Method: "External" },
      { Format: "pgssub", Method: "Encode" },
      { Format: "dvdsub", Method: "Encode" },
    ],
  };
}
