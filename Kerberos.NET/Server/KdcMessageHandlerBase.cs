﻿using Kerberos.NET.Entities;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kerberos.NET.Server
{
    using PreAuthHandlerConstructor = Func<IRealmService, KdcPreAuthenticationHandlerBase>;

    public abstract class KdcMessageHandlerBase
    {
        private readonly ConcurrentDictionary<PaDataType, PreAuthHandlerConstructor> preAuthHandlers =
            new ConcurrentDictionary<PaDataType, PreAuthHandlerConstructor>();

        private readonly IMemoryOwner<byte> messagePool;
        private readonly int messageLength;

        protected ListenerOptions Options { get; }

        protected IRealmService RealmService { get; private set; }

        public IDictionary<PaDataType, PreAuthHandlerConstructor> PreAuthHandlers
        {
            get => preAuthHandlers;
        }

        protected abstract MessageType MessageType { get; }

        public abstract Task<PreAuthenticationContext> ValidateTicketRequest(IKerberosMessage message);

        protected KdcMessageHandlerBase(ReadOnlySequence<byte> message, ListenerOptions options)
        {
            messageLength = (int)message.Length;
            messagePool = MemoryPool<byte>.Shared.Rent(messageLength);

            message.CopyTo(messagePool.Memory.Span.Slice(0, messageLength));

            Options = options;
        }

        protected async Task SetRealmContext(string realm)
        {
            RealmService = await Options.RealmLocator(realm);
        }

        private Task<IKerberosMessage> DecodeMessage(ReadOnlyMemory<byte> message)
        {
            var decoded = DecodeMessageCore(message);

            if (decoded.KerberosProtocolVersionNumber != 5)
            {
                throw new InvalidOperationException($"Message version should be set to v5. Actual: {decoded.KerberosProtocolVersionNumber}");
            }

            if (decoded.KerberosMessageType != MessageType)
            {
                throw new InvalidOperationException($"MessageType should match application class. Actual: {decoded.KerberosMessageType}; Expected: {MessageType}");
            }

            return Task.FromResult(decoded);
        }

        protected abstract IKerberosMessage DecodeMessageCore(ReadOnlyMemory<byte> message);

        public async Task<IKerberosMessage> DecodeMessage()
        {
            return await DecodeMessage(messagePool.Memory.Slice(0, messageLength));
        }

        public virtual async Task<ReadOnlyMemory<byte>> Execute()
        {
            try
            {
                var message = await DecodeMessage();

                var context = await ValidateTicketRequest(message);

                return await ExecuteCore(message, context);
            }
            catch (Exception ex)
            {
                return GenerateGenericError(ex, Options);
            }
            finally
            {
                messagePool.Dispose();
            }
        }

        public abstract Task<ReadOnlyMemory<byte>> ExecuteCore(IKerberosMessage message, PreAuthenticationContext context);

        internal static ReadOnlyMemory<byte> GenerateGenericError(Exception ex, ListenerOptions options)
        {
            return GenerateError(KerberosErrorCode.KRB_ERR_GENERIC, options.IsDebug ? $"[Server] {ex}" : null, options.DefaultRealm, "krbtgt");
        }

        internal static ReadOnlyMemory<byte> GenerateError(KerberosErrorCode code, string error, string realm, string sname)
        {
            var krbErr = new KrbError()
            {
                ErrorCode = code,
                EText = error,
                Realm = realm,
                SName = new KrbPrincipalName
                {
                    Type = PrincipalNameType.NT_SRV_INST,
                    Name = new[] {
                        sname, realm
                    }
                }
            };

            krbErr.StampServerTime();

            return krbErr.EncodeApplication();
        }

        internal void RegisterPreAuthHandlers(ConcurrentDictionary<PaDataType, PreAuthHandlerConstructor> preAuthHandlers)
        {
            foreach (var handler in preAuthHandlers)
            {
                this.preAuthHandlers[handler.Key] = handler.Value;
            }
        }
    }
}
