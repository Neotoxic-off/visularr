#nullable disable

#pragma warning disable CS1591

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common;
using MediaBrowser.Controller.LiveTv;

namespace Jellyfin.LiveTv.TunerHosts.HdHomerun
{
    public sealed class HdHomerunManager : IDisposable
    {
        public const int HdHomeRunPort = 65001;

        // Message constants
        private const byte GetSetName = 3;
        private const byte GetSetValue = 4;
        private const byte GetSetLockkey = 21;
        private const ushort GetSetRequest = 4;
        private const ushort GetSetReply = 5;

        private uint? _lockkey = null;
        private int _activeTuner = -1;
        private IPEndPoint _remoteEndPoint;

        private TcpClient _tcpClient;

        public void Dispose()
        {
            using (TcpClient socket = _tcpClient)
            {
                if (socket is not null)
                {
                    _tcpClient = null;

                    StopStreaming(socket).GetAwaiter().GetResult();
                }
            }
        }

        public async Task<bool> CheckTunerAvailability(IPAddress remoteIP, int tuner, CancellationToken cancellationToken)
        {
            using TcpClient client = new TcpClient();
            await client.ConnectAsync(remoteIP, HdHomeRunPort, cancellationToken).ConfigureAwait(false);

            using NetworkStream stream = client.GetStream();
            return await CheckTunerAvailability(stream, tuner, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<bool> CheckTunerAvailability(NetworkStream stream, int tuner, CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                int msgLen = WriteGetMessage(buffer, tuner, "lockkey");
                await stream.WriteAsync(buffer.AsMemory(0, msgLen), cancellationToken).ConfigureAwait(false);

                int receivedBytes = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                return VerifyReturnValueOfGetSet(buffer.AsSpan(0, receivedBytes), "none");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public async Task StartStreaming(IPAddress remoteIP, IPAddress localIP, int localPort, IHdHomerunChannelCommands commands, int numTuners, CancellationToken cancellationToken)
        {
            _remoteEndPoint = new IPEndPoint(remoteIP, HdHomeRunPort);

            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(_remoteEndPoint, cancellationToken).ConfigureAwait(false);

            if (!_lockkey.HasValue)
            {
                _lockkey = (uint)Random.Shared.Next();
            }

            uint lockKeyValue = _lockkey.Value;
            NetworkStream stream = _tcpClient.GetStream();

            byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                for (int i = 0; i < numTuners; ++i)
                {
                    if (!await CheckTunerAvailability(stream, i, cancellationToken).ConfigureAwait(false))
                    {
                        continue;
                    }

                    _activeTuner = i;
                    string lockKeyString = string.Format(CultureInfo.InvariantCulture, "{0:d}", lockKeyValue);
                    int lockkeyMsgLen = WriteSetMessage(buffer, i, "lockkey", lockKeyString, null);
                    await stream.WriteAsync(buffer.AsMemory(0, lockkeyMsgLen), cancellationToken).ConfigureAwait(false);
                    int receivedBytes = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                    // parse response to make sure it worked
                    if (!TryGetReturnValueOfGetSet(buffer.AsSpan(0, receivedBytes), out _))
                    {
                        continue;
                    }

                    foreach ((string CommandName, string CommandValue) command in commands.GetCommands())
                    {
                        int channelMsgLen = WriteSetMessage(buffer, i, command.CommandName, command.CommandValue, lockKeyValue);
                        await stream.WriteAsync(buffer.AsMemory(0, channelMsgLen), cancellationToken).ConfigureAwait(false);
                        receivedBytes = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                        // parse response to make sure it worked
                        if (!TryGetReturnValueOfGetSet(buffer.AsSpan(0, receivedBytes), out _))
                        {
                            await ReleaseLockkey(_tcpClient, lockKeyValue).ConfigureAwait(false);
                        }
                    }

                    string targetValue = string.Format(CultureInfo.InvariantCulture, "rtp://{0}:{1}", localIP, localPort);
                    int targetMsgLen = WriteSetMessage(buffer, i, "target", targetValue, lockKeyValue);

                    await stream.WriteAsync(buffer.AsMemory(0, targetMsgLen), cancellationToken).ConfigureAwait(false);
                    receivedBytes = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                    // parse response to make sure it worked
                    if (!TryGetReturnValueOfGetSet(buffer.AsSpan(0, receivedBytes), out _))
                    {
                        await ReleaseLockkey(_tcpClient, lockKeyValue).ConfigureAwait(false);
                        continue;
                    }

                    return;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            _activeTuner = -1;
            throw new LiveTvConflictException("No tuners available");
        }

        public async Task ChangeChannel(IHdHomerunChannelCommands commands, CancellationToken cancellationToken)
        {
            if (!_lockkey.HasValue)
            {
                return;
            }

            using TcpClient tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(_remoteEndPoint, cancellationToken).ConfigureAwait(false);

            using NetworkStream stream = tcpClient.GetStream();
            IEnumerable<(string CommandName, string CommandValue)> commandList = commands.GetCommands();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                foreach ((string CommandName, string CommandValue) command in commandList)
                {
                    int channelMsgLen = WriteSetMessage(buffer, _activeTuner, command.CommandName, command.CommandValue, _lockkey);
                    await stream.WriteAsync(buffer.AsMemory(0, channelMsgLen), cancellationToken).ConfigureAwait(false);
                    int receivedBytes = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                    // parse response to make sure it worked
                    if (!TryGetReturnValueOfGetSet(buffer.AsSpan(0, receivedBytes), out _))
                    {
                        return;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public Task StopStreaming(TcpClient client)
        {
            uint? lockKey = _lockkey;

            if (!lockKey.HasValue)
            {
                return Task.CompletedTask;
            }

            return ReleaseLockkey(client, lockKey.Value);
        }

        private async Task ReleaseLockkey(TcpClient client, uint lockKeyValue)
        {
            NetworkStream stream = client.GetStream();

            byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                int releaseTargetLen = WriteSetMessage(buffer, _activeTuner, "target", "none", lockKeyValue);
                await stream.WriteAsync(buffer.AsMemory(0, releaseTargetLen)).ConfigureAwait(false);

                await stream.ReadAsync(buffer).ConfigureAwait(false);
                int releaseKeyMsgLen = WriteSetMessage(buffer, _activeTuner, "lockkey", "none", lockKeyValue);
                _lockkey = null;
                await stream.WriteAsync(buffer.AsMemory(0, releaseKeyMsgLen)).ConfigureAwait(false);
                await stream.ReadAsync(buffer).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        internal static int WriteGetMessage(Span<byte> buffer, int tuner, string name)
        {
            string byteName = string.Format(CultureInfo.InvariantCulture, "/tuner{0}/{1}", tuner, name);
            int offset = WriteHeaderAndPayload(buffer, byteName);
            return FinishPacket(buffer, offset);
        }

        internal static int WriteSetMessage(Span<byte> buffer, int tuner, string name, string value, uint? lockkey)
        {
            string byteName = string.Format(CultureInfo.InvariantCulture, "/tuner{0}/{1}", tuner, name);
            int offset = WriteHeaderAndPayload(buffer, byteName);

            buffer[offset++] = GetSetValue;
            offset += WriteNullTerminatedString(buffer.Slice(offset), value);

            if (lockkey.HasValue)
            {
                buffer[offset++] = GetSetLockkey;
                buffer[offset++] = 4;
                BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(offset), lockkey.Value);
                offset += 4;
            }

            return FinishPacket(buffer, offset);
        }

        internal static int WriteNullTerminatedString(Span<byte> buffer, ReadOnlySpan<char> payload)
        {
            int len = Encoding.UTF8.GetBytes(payload, buffer.Slice(1)) + 1;

            // TODO: variable length: this can be 2 bytes if len > 127
            // Write length in front of value
            buffer[0] = Convert.ToByte(len);

            // null-terminate
            buffer[len++] = 0;

            return len;
        }

        private static int WriteHeaderAndPayload(Span<byte> buffer, ReadOnlySpan<char> payload)
        {
            // Packet type
            BinaryPrimitives.WriteUInt16BigEndian(buffer, GetSetRequest);

            // We write the payload length at the end
            int offset = 4;

            // Tag
            buffer[offset++] = GetSetName;

            // Payload length + data
            int strLen = WriteNullTerminatedString(buffer.Slice(offset), payload);
            offset += strLen;

            return offset;
        }

        private static int FinishPacket(Span<byte> buffer, int offset)
        {
            // Payload length
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(2), (ushort)(offset - 4));

            // calculate crc and insert at the end of the message
            uint crc = Crc32.Compute(buffer.Slice(0, offset));
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(offset), crc);

            return offset + 4;
        }

        internal static bool VerifyReturnValueOfGetSet(ReadOnlySpan<byte> buffer, string expected)
        {
            return TryGetReturnValueOfGetSet(buffer, out ReadOnlySpan<byte> value)
                && string.Equals(Encoding.UTF8.GetString(value), expected, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryGetReturnValueOfGetSet(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> value)
        {
            value = ReadOnlySpan<byte>.Empty;

            if (buffer.Length < 8)
            {
                return false;
            }

            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(buffer[^4..]);
            if (crc != Crc32.Compute(buffer[..^4]))
            {
                return false;
            }

            if (BinaryPrimitives.ReadUInt16BigEndian(buffer) != GetSetReply)
            {
                return false;
            }

            ushort msgLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2));
            if (buffer.Length != 2 + 2 + 4 + msgLength)
            {
                return false;
            }

            int offset = 4;
            if (buffer[offset++] != GetSetName)
            {
                return false;
            }

            byte nameLength = buffer[offset++];
            if (buffer.Length < 4 + 1 + offset + nameLength)
            {
                return false;
            }

            offset += nameLength;

            if (buffer[offset++] != GetSetValue)
            {
                return false;
            }

            byte valueLength = buffer[offset++];
            if (buffer.Length < 4 + offset + valueLength)
            {
                return false;
            }

            // remove null terminator
            value = buffer.Slice(offset, valueLength - 1);
            return true;
        }
    }
}
