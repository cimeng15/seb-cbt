/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Modified for CBT integration (https://cbt.smkdata.sch.id).
 */

namespace SafeExamBrowser.Client.Responsibilities
{
	/// <summary>
	/// Defines the outcome of a quit/unlock password verification, so that the caller can present an appropriate message.
	/// </summary>
	internal enum QuitPasswordVerification
	{
		/// <summary>
		/// The password could not be verified because neither the CBT kiosk endpoint(s) nor a local quit password were available.
		/// </summary>
		Unavailable,

		/// <summary>
		/// The password was verified and matched.
		/// </summary>
		Valid,

		/// <summary>
		/// The password was verified and did not match.
		/// </summary>
		Invalid,

		/// <summary>
		/// The password configured on the CBT kiosk endpoint has expired and must therefore be rejected.
		/// </summary>
		Expired
	}
}
