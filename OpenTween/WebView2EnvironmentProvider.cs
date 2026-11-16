// OpenTween - Client of Twitter
// Copyright (c) 2026 OpenTween contributors
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
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace OpenTween
{
    /// <summary>
    /// アプリ内の WebView2 で共有する <see cref="CoreWebView2Environment"/> を提供します
    /// </summary>
    /// <remarks>
    /// 同じユーザーデータフォルダを異なるオプションで開くと初期化に失敗するため、環境は 1 つだけ作成して使い回す。
    /// UI スレッドから呼び出すこと。
    /// </remarks>
    public static class WebView2EnvironmentProvider
    {
        private static Task<CoreWebView2Environment>? environmentTask;

        public static Task<CoreWebView2Environment> GetEnvironmentAsync()
        {
            var task = environmentTask;
            if (task == null || task.IsFaulted || task.IsCanceled)
                environmentTask = task = CreateEnvironmentAsync();

            return task;
        }

        private static Task<CoreWebView2Environment> CreateEnvironmentAsync()
        {
            var options = new CoreWebView2EnvironmentOptions();

            // 仮想化環境（Parallels Desktop 等）では GPU レンダリングが動作しないため
            // ソフトウェアレンダリングにフォールバックする。
            // 実機では GPU レンダリングを使用する（swiftshader は CPU 負荷が高いため）
            if (IsVirtualizedEnvironment())
                options.AdditionalBrowserArguments = "--disable-gpu --disable-gpu-compositing --disable-gpu-sandbox --use-gl=swiftshader";

            return CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenTween",
                    "WebView2"
                ),
                options: options
            );
        }

        /// <summary>仮想マシン上で動作しているかを BIOS 情報から判定する</summary>
        private static bool IsVirtualizedEnvironment()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                if (key == null)
                    return false;

                var manufacturer = key.GetValue("SystemManufacturer") as string ?? "";
                var productName = key.GetValue("SystemProductName") as string ?? "";
                var combined = manufacturer + " " + productName;

                return combined.IndexOf("Parallels", StringComparison.OrdinalIgnoreCase) >= 0
                    || combined.IndexOf("VMware", StringComparison.OrdinalIgnoreCase) >= 0
                    || combined.IndexOf("VirtualBox", StringComparison.OrdinalIgnoreCase) >= 0
                    || combined.IndexOf("QEMU", StringComparison.OrdinalIgnoreCase) >= 0
                    || combined.IndexOf("Virtual Machine", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
