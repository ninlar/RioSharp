﻿using System;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RioSharp
{
    public unsafe class RioSocket : IDisposable
    {
        IntPtr _requestQueue;
        internal IntPtr Socket;
        internal RioFixedBufferPool SendBufferPool, ReceiveBufferPool, AdressPool;

        internal RioSocket(RioFixedBufferPool sendBufferPool, RioFixedBufferPool receiveBufferPool, RioFixedBufferPool adressBufferPool,
            uint maxOutstandingReceive, uint maxOutstandingSend, IntPtr SendCompletionQueue, IntPtr ReceiveCompletionQueue,
            ADDRESS_FAMILIES adressFam, SOCKET_TYPE sockType, PROTOCOL protocol)
        {
            if ((Socket = WinSock.WSASocket(adressFam, sockType, protocol, IntPtr.Zero, 0, SOCKET_FLAGS.REGISTERED_IO | SOCKET_FLAGS.WSA_FLAG_OVERLAPPED)) == IntPtr.Zero)
                WinSock.ThrowLastWSAError();

            SendBufferPool = sendBufferPool;
            ReceiveBufferPool = receiveBufferPool;
            AdressPool = adressBufferPool;

            _requestQueue = RioStatic.CreateRequestQueue(Socket, maxOutstandingReceive - 1, 1, maxOutstandingSend - 1, 1, ReceiveCompletionQueue, SendCompletionQueue, GetHashCode());
            WinSock.ThrowLastWSAError();
        }

        public RioBufferSegment Send(RioBufferSegment Segment)
        {
            Segment.SetNotComplete();
            if (!RioStatic.Send(_requestQueue, Segment.SegmentPointer, 1, RIO_SEND_FLAGS.NONE, Segment.Index))
                WinSock.ThrowLastWSAError();

            return Segment;
        }

        public unsafe RioBufferSegment Send(byte[] buffer)
        {
            var currentSegment = SendBufferPool.GetBuffer();
            fixed (byte* p = &buffer[0])
            {
                Unsafe.CopyBlock(currentSegment.RawPointer, p, (uint)buffer.Length);
            }
            currentSegment.SegmentPointer->Length = buffer.Length;
            Send(currentSegment, RIO_SEND_FLAGS.NONE);
            currentSegment.DisposeWhenComplete();
            return currentSegment;
        }

        public unsafe RioBufferSegment Send(byte[] buffer, IPEndPoint remoteAdress)
        {
            var currentSegment = SendBufferPool.GetBuffer();
            fixed (byte* p = &buffer[0])
            {
                Unsafe.CopyBlock(currentSegment.RawPointer, p, (uint)buffer.Length);
            }
            currentSegment.SegmentPointer->Length = buffer.Length;
            Send(currentSegment, remoteAdress, RIO_SEND_FLAGS.NONE);
            currentSegment.DisposeWhenComplete();
            return currentSegment;
        }

        internal void Send(RioBufferSegment segment, RIO_SEND_FLAGS flags)
        {
            segment.SetNotComplete();
            if (!RioStatic.Send(_requestQueue, segment.SegmentPointer, 1, flags, segment.Index))
                WinSock.ThrowLastWSAError();
        }

        internal void Send(RioBufferSegment segment, IPEndPoint remoteAdress, RIO_SEND_FLAGS flags)
        {
            var adresssegment = AllocateAdress(remoteAdress);
            Send(segment, adresssegment, flags);
            adresssegment.Dispose();
        }

        internal void Send(RioBufferSegment segment, RioBufferSegment remoteAdress, RIO_SEND_FLAGS flags)
        {
            segment.SetNotComplete();
            if (!RioStatic.SendEx(_requestQueue, segment.SegmentPointer, 1, RIO_BUF.NullSegment, remoteAdress.SegmentPointer, RIO_BUF.NullSegment, RIO_BUF.NullSegment, flags, segment.Index))
                WinSock.ThrowLastWSAError();
        }

        public RioBufferSegment BeginReceive(RioBufferSegment segment)
        {
            segment.SegmentPointer->Length = segment.TotalLength;
            segment.SetNotComplete();
            if (!RioStatic.Receive(_requestQueue, segment.SegmentPointer, 1, RIO_RECEIVE_FLAGS.NONE, segment.Index))
                WinSock.ThrowLastWSAError();

            return segment;
        }

        public unsafe void Flush()
        {
            if (!RioStatic.Send(_requestQueue, RIO_BUF.NullSegment, 0, RIO_SEND_FLAGS.COMMIT_ONLY, 0))
                WinSock.ThrowLastWSAError();
        }

        public RioBufferSegment AllocateAdress(IPEndPoint remoteAdress)
        {
            var adresssegment = AdressPool.GetBuffer();
            SOCKADDR_INET* adress = (SOCKADDR_INET*)adresssegment.Datapointer;
            var adressBytes = remoteAdress.Address.GetAddressBytes();

            if (remoteAdress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                adress->Ipv4.sin_family = ADDRESS_FAMILIES.AF_INET;
                adress->Ipv4.sin_port = WinSock.htons((ushort)remoteAdress.Port);

                fixed (byte* a = adressBytes)
                    Unsafe.CopyBlock(adress->Ipv4.sin_addr.Address, a, (uint)adressBytes.Length);
            }

            return adresssegment;
        }

        public virtual void Dispose()
        {
            WinSock.closesocket(Socket);
        }


        public void SetTcpNoDelay(bool value)
        {
            int v = value ? 1 : 0;
            if (WinSock.setsockopt(Socket, WinSock.IPPROTO_TCP, WinSock.TCP_NODELAY, &v, 4) != 0)
                WinSock.ThrowLastWSAError();
        }

        public void SetLoopbackFastPath(bool value)
        {
            int v = value ? 1 : 0;
            uint dwBytes = 0;

            if (WinSock.WSAIoctlGeneral2(Socket, WinSock.SIO_LOOPBACK_FAST_PATH, &v, sizeof(int), (void*)0, 0, out dwBytes, IntPtr.Zero, IntPtr.Zero) != 0)
                WinSock.ThrowLastWSAError();

        }


        public int SetSocketOption(IPPROTO_IP_SocketOptions option, void* value, int valueLength) => WinSock.setsockopt(Socket, WinSock.IPPROTO_IP, (int)option, value, valueLength);

        public int SetSocketOption(IPPROTO_IPV6_SocketOptions option, void* value, int valueLength) => WinSock.setsockopt(Socket, WinSock.IPPROTO_IPV6, (int)option, value, valueLength);

        public int SetSocketOption(IPPROTO_TCP_SocketOptions option, void* value, int valueLength) => WinSock.setsockopt(Socket, WinSock.IPPROTO_TCP, (int)option, value, valueLength);

        public int SetSocketOption(IPPROTO_UDP_SocketOptions option, void* value, int valueLength) => WinSock.setsockopt(Socket, WinSock.IPPROTO_UDP, (int)option, value, valueLength);

        public int SetSocketOption(SOL_SOCKET_SocketOptions option, void* value, int valueLength) => WinSock.setsockopt(Socket, WinSock.SOL_SOCKET, (int)option, value, valueLength);

        public int SetSocketOption(MCAST_SocketOptions option, void* value, int valueLength) => WinSock.setsockopt(Socket, WinSock.IPPROTO_IP, (int)option, value, valueLength);


        public int GetSocketOption(MCAST_SocketOptions option, IntPtr value, int valueLength) => WinSock.getsockopt(Socket, WinSock.IPPROTO_IP, (int)option, (char*)value.ToPointer(), &valueLength);

        public int GetSocketOption(IPPROTO_IP_SocketOptions option, IntPtr value, int valueLength) => WinSock.getsockopt(Socket, WinSock.IPPROTO_IP, (int)option, (char*)value.ToPointer(), &valueLength);

        public int GetSocketOption(IPPROTO_IPV6_SocketOptions option, IntPtr value, int valueLength) => WinSock.getsockopt(Socket, WinSock.IPPROTO_IPV6, (int)option, (char*)value.ToPointer(), &valueLength);

        public int GetSocketOption(IPPROTO_TCP_SocketOptions option, IntPtr value, int valueLength) => WinSock.getsockopt(Socket, WinSock.IPPROTO_TCP, (int)option, (char*)value.ToPointer(), &valueLength);

        public int GetSocketOption(IPPROTO_UDP_SocketOptions option, IntPtr value, int valueLength) => WinSock.getsockopt(Socket, WinSock.IPPROTO_UDP, (int)option, (char*)value.ToPointer(), &valueLength);

        public int GetSocketOption(SOL_SOCKET_SocketOptions option, IntPtr value, int valueLength) => WinSock.getsockopt(Socket, WinSock.SOL_SOCKET, (int)option, (char*)value.ToPointer(), &valueLength);
    }
}

