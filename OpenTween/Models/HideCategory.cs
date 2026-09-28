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
using System.Xml.Serialization;

namespace OpenTween.Models
{
    /// <summary>
    /// 非表示カテゴリ (「ニュース」など、まとめて表示を隠すユーザーのグループ)
    /// </summary>
    public class HideCategory
    {
        public string Name { get; set; } = "";

        /// <summary>カテゴリに含まれるユーザーのスクリーンネーム (先頭の @ は不要)</summary>
        [XmlArrayItem("ScreenName")]
        public string[] ScreenNames { get; set; } = Array.Empty<string>();
    }
}
