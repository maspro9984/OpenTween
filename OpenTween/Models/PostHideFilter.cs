// OpenTween - Client of Twitter
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

namespace OpenTween.Models
{
    public static class PostHideFilter
    {
        /// <summary>
        /// 発言を表示しない場合に true を返す判定関数を生成する
        /// </summary>
        /// <returns>フィルター表示が off の場合や、非表示にする条件が無い場合は null</returns>
        public static Func<PostClass, bool>? Create(TabHideSettings settings, IEnumerable<HideCategory> categories)
        {
            if (!settings.Enabled)
                return null;

            var hiddenScreenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var categoryName in settings.HiddenCategories)
            {
                var category = categories.FirstOrDefault(x => x.Name == categoryName);
                if (category == null)
                    continue;

                foreach (var screenName in category.ScreenNames)
                {
                    var normalized = NormalizeScreenName(screenName);
                    if (normalized.Length > 0)
                        hiddenScreenNames.Add(normalized);
                }
            }

            var hideRetweets = settings.HideRetweets;
            var hidePromoted = settings.HidePromoted;
            var hideNonFollowing = settings.HideNonFollowing;

            if (!hideRetweets && !hidePromoted && !hideNonFollowing && hiddenScreenNames.Count == 0)
                return null;

            return post =>
            {
                if (hideRetweets && post.RetweetedId != null)
                    return true;

                if (hidePromoted && post.IsPromoted)
                    return true;

                // リツイートはリツイートした人 (フォロー中) で判断するため RT の非表示設定に委ねる。
                // フォロー状態が不明な発言 (IsAuthorFollowed が null) は隠さない
                if (hideNonFollowing && post.RetweetedId == null && !post.IsMe && post.IsAuthorFollowed == false)
                    return true;

                if (hiddenScreenNames.Count > 0)
                {
                    if (hiddenScreenNames.Contains(post.ScreenName))
                        return true;

                    if (post.RetweetedBy != null && hiddenScreenNames.Contains(post.RetweetedBy))
                        return true;
                }

                return false;
            };
        }

        public static string NormalizeScreenName(string screenName)
            => screenName.Trim().TrimStart('@').Trim();
    }
}
