// OpenTween - Client of Twitter
// Copyright (c) 2024 kim_upsilon (@kim_upsilon) <https://upsilo.net/~upsilon/>
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

using System.Collections.Generic;
using OpenTween.Api;
using OpenTween.Api.DataModel;
using OpenTween.Models;

namespace OpenTween.SocialProtocol.Twitter
{
    public class TwitterAccountState : ISocialAccountState
    {
        public TwitterUserId UserId { get; private set; }

        PersonId ISocialAccountState.UserId
            => this.UserId;

        public string UserName { get; private set; }

        public int? FollowersCount { get; private set; }

        public int? FriendsCount { get; private set; }

        public int? StatusesCount { get; private set; }

        /// <summary>
        /// 有料プラン (X Premium) の認証済みアカウントか否か。長文ツイートの投稿可否の判定に使用する
        /// </summary>
        public bool IsBlueVerified { get; private set; }

        public ISet<PersonId> FollowerIds { get; set; } = new HashSet<PersonId>();

        public ISet<PersonId> BlockedUserIds { get; set; } = new HashSet<PersonId>();

        public ISet<PersonId> MutedUserIds { get; set; } = new HashSet<PersonId>();

        public ISet<TwitterUserId> NoRetweetUserIds { get; set; } = new HashSet<TwitterUserId>();

        public TwitterConfiguration Configuration { get; } = TwitterConfiguration.DefaultConfiguration();

        public TwitterTextConfiguration TextConfiguration { get; } = TwitterTextConfiguration.DefaultConfiguration();

        public TwitterRateLimitCollection RateLimits { get; } = new();

        RateLimitCollection ISocialAccountState.RateLimits
            => this.RateLimits;

        public bool HasUnrecoverableError { get; set; } = true;

        public TwitterAccountState()
            : this(new("0"), "")
        {
        }

        public TwitterAccountState(TwitterUserId userId, string userName)
        {
            this.UserId = userId;
            this.UserName = userName;
        }

        public void UpdateFromSettings(UserAccount accountSettings)
        {
            this.UserId = new(accountSettings.UserId);
            this.UserName = accountSettings.Username;
        }

        /// <summary>ユーザー情報を更新します</summary>
        public void UpdateFromUser(TwitterUser self)
        {
            this.UserId = new(self.IdStr);
            this.UserName = self.ScreenName;
            this.FollowersCount = self.FollowersCount;
            this.FriendsCount = self.FriendsCount;
            this.StatusesCount = self.StatusesCount;
            this.IsBlueVerified = self.IsBlueVerified;
        }
    }
}
