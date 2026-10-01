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

using OpenTween.Api.DataModel;
using OpenTween.Models;
using Xunit;

namespace OpenTween.SocialProtocol.Twitter
{
    public class TwitterAccountStateTest
    {
        [Fact]
        public void UpdateFromUser_Test()
        {
            var accountState = new TwitterAccountState();

            var twitterUser = new TwitterUser
            {
                IdStr = "514241801",
                ScreenName = "OpenTween",
                StatusesCount = 31,
                FriendsCount = 1,
                FollowersCount = 302,
                IsBlueVerified = true,
            };
            accountState.UpdateFromUser(twitterUser);

            Assert.Equal(new TwitterUserId("514241801"), accountState.UserId);
            Assert.Equal("OpenTween", accountState.UserName);
            Assert.Equal(31, accountState.StatusesCount);
            Assert.Equal(1, accountState.FriendsCount);
            Assert.Equal(302, accountState.FollowersCount);
            Assert.True(accountState.IsBlueVerified);
        }
    }
}
