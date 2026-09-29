// OpenTween - Client of Twitter
// Copyright (c) 2012 kim_upsilon (@kim_upsilon) <https://upsilo.net/~upsilon/>
// All rights reserved.
//
// This file is part of OpenTween.
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation; either version 3 of the License, or (at your option)
// any later version.
//
// This program is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License
// for more details.
//
// You should have received a copy of the GNU General Public License along
// with this program. If not, see <http://www.gnu.org/licenses/>, or write to
// the Free Software Foundation, Inc., 51 Franklin Street - Fifth Floor,
// Boston, MA 02110-1301, USA.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenTween.Connection;
using OpenTween.Models;

namespace OpenTween.Thumbnail.Services
{
    public class Pixiv : MetaThumbnailService
    {
        public static readonly string UrlPattern =
            @"^https?://(www|touch)\.pixiv\.net/(member_illust|index)\.php\?(?=.*mode=(medium|big))(?=.*illust_id=(?<illustId>[0-9]+)).*$";

        public Pixiv()
            : base(Pixiv.UrlPattern)
        {
        }

        public Pixiv(HttpClient? http)
            : base(http, Pixiv.UrlPattern)
        {
        }

        public override async Task<ThumbnailInfo?> GetThumbnailInfoAsync(string url, PostClass post, CancellationToken token)
        {
            var thumb = await base.GetThumbnailInfoAsync(url, post, token)
                .ConfigureAwait(false);

            if (thumb == null) return null;

            return thumb with
            {
                Loader = new ThumbnailLoader(new(thumb.MediaPageUrl), new(thumb.ThumbnailImageUrl)),
            };
        }

        public class ThumbnailLoader : IProgressReportingThumbnailLoader
        {
            private readonly Uri mediaPageUri;
            private readonly Uri thumbnailImageUri;

            public ThumbnailLoader(Uri mediaPageUri, Uri thumbnailImageUri)
            {
                this.mediaPageUri = mediaPageUri;
                this.thumbnailImageUri = thumbnailImageUri;
            }

            public Task<MemoryImage> Load(HttpClient http, CancellationToken cancellationToken)
                => this.Load(http, null, cancellationToken);

            public async Task<MemoryImage> Load(HttpClient http, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, this.thumbnailImageUri);

                request.Headers.Add("User-Agent", Networking.GetUserAgentString(fakeMSIE: true));
                request.Headers.Referrer = this.mediaPageUri;

                return await SimpleThumbnailLoader.LoadWithProgressAsync(http, request, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
