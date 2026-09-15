/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Modified for CBT integration (https://cbt.smkdata.sch.id).
 */

namespace SafeExamBrowser.Settings
{
	/// <summary>
	/// Compile-time defaults for the CBT integration. These values are applied when no configuration file is loaded, so that the
	/// application works out of the box without a .seb configuration file. Any value loaded from a configuration file overrides them.
	/// </summary>
	public static class CbtDefaults
	{
		/// <summary>
		/// The CBT base URL the exam client is locked to.
		/// </summary>
		public const string BaseUrl = "https://cbt.smkdata.sch.id";

		/// <summary>
		/// The CBT kiosk settings endpoint from which the quit/unlock password is retrieved.
		/// </summary>
		public const string KioskUrl = "https://cbt.smkdata.sch.id/api/kiosk/settings";

		/// <summary>
		/// The default timeout (in milliseconds) when querying the CBT kiosk settings endpoint.
		/// </summary>
		public const int KioskTimeout = 5000;

		/// <summary>
		/// The default number of attempts when querying the CBT kiosk settings endpoint.
		/// </summary>
		public const int KioskAttempts = 3;

		/// <summary>
		/// The default delay (in milliseconds) between attempts when querying the CBT kiosk settings endpoint.
		/// </summary>
		public const int KioskAttemptInterval = 1000;
	}
}
