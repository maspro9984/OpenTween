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
using System.Xml.Serialization;

namespace OpenTween.Models
{
    /// <summary>
    /// タブ毎に設定する、タイムラインに表示しない発言の種類
    /// </summary>
    /// <remarks>
    /// 発言そのものはタブに残したまま、一覧への表示のみを抑制する
    /// </remarks>
    public class TabHideSettings
    {
        /// <summary>
        /// フィルター表示の on/off。off の場合は各条件の設定を保持したまま全ての発言を表示する
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>リツイートを表示しない</summary>
        public bool HideRetweets { get; set; }

        /// <summary>プロモーション (広告) を表示しない</summary>
        public bool HidePromoted { get; set; }

        /// <summary>フォローしていないユーザーの発言を表示しない</summary>
        public bool HideNonFollowing { get; set; }

        /// <summary>表示しない非表示カテゴリの名前</summary>
        [XmlArrayItem("Category")]
        public string[] HiddenCategories { get; set; } = Array.Empty<string>();

        /// <summary>フォローしている人の発言のみ表示する (RT・広告・フォロー外を全て隠す) 状態であるか</summary>
        [XmlIgnore]
        public bool FollowingOnly
        {
            get => this.HideRetweets && this.HidePromoted && this.HideNonFollowing;
            set
            {
                this.HideRetweets = value;
                this.HidePromoted = value;
                this.HideNonFollowing = value;
            }
        }

        /// <summary>いずれかの非表示条件が設定されているか (フィルター表示の on/off は問わない)</summary>
        [XmlIgnore]
        public bool HasAnyCondition
            => this.HideRetweets || this.HidePromoted || this.HideNonFollowing || this.HiddenCategories.Length > 0;

        /// <summary>フィルター表示が on で、かつ非表示条件が設定されているか</summary>
        [XmlIgnore]
        public bool IsActive
            => this.Enabled && this.HasAnyCondition;

        public TabHideSettings Clone()
            => new()
            {
                Enabled = this.Enabled,
                HideRetweets = this.HideRetweets,
                HidePromoted = this.HidePromoted,
                HideNonFollowing = this.HideNonFollowing,
                HiddenCategories = this.HiddenCategories.ToArray(),
            };
    }
}
