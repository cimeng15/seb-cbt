/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Modified for CBT integration (https://cbt.smkdata.sch.id).
 */

using SafeExamBrowser.Server.Contracts.Data;

namespace SafeExamBrowser.Server.Contracts
{
	/// <summary>
	/// Defines the functionality for retrieving the quit/unlock settings from a CBT kiosk settings endpoint.
	/// </summary>
	public interface ICbtKioskClient
	{
		/// <summary>
		/// Retrieves the quit/unlock settings from the given endpoint. Never throws, but returns a result with
		/// <see cref="KioskSettings.Success"/> set to <c>false</c> and a descriptive message on failure.
		/// </summary>
		KioskSettings GetSettings(string url, int timeout = 5000);
	}
}
