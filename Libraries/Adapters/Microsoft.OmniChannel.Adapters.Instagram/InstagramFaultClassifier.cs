// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Classifies an outbound Instagram Send failure as transient (retry) vs terminal (fail loudly). A dead token
    /// or capability error surfaces as a 4xx <see cref="InstagramSendException"/> and must NOT be retried into
    /// silence; a 429/5xx is a transient blip worth a few retries.
    /// </summary>
    public static class InstagramFaultClassifier
    {
        public static bool IsTransientSendFault(Exception ex)
        {
            switch (ex)
            {
                case null:
                    return false;
                case HttpRequestException:
                    return true;
                case TaskCanceledException:
                    return true;
                case InstagramSendException send:
                    return send.StatusCode == 429 || send.StatusCode >= 500;
                default:
                    return false;
            }
        }
    }
}
