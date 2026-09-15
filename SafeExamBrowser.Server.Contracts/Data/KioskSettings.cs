/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Modified for CBT integration (https://cbt.smkdata.sch.id).
 */

using System;

namespace SafeExamBrowser.Server.Contracts.Data
{
	/// <summary>
	/// Defines the quit/unlock settings retrieved from a CBT kiosk settings endpoint.
	/// </summary>
	public class KioskSettings
	{
		/// <summary>
		/// The plain-text exit password currently configured by the exam administrator. Only meaningful when
		/// <see cref="Success"/> is <c>true</c> and <see cref="IsExpired"/> is <c>false</c>.
		/// </summary>
		public string ExitPassword { get; set; }

		/// <summary>
		/// The point in time at which the exit password expires, if provided by the endpoint.
		/// </summary>
		public DateTime? PasswordExpiresAt { get; set; }

		/// <summary>
		/// Indicates whether the exit password is currently expired and must therefore be rejected.
		/// </summary>
		public bool IsExpired { get; set; }

		/// <summary>
		/// The message describing why the retrieval failed, or <c>default</c> on success.
		/// </summary>
		public string Message { get; set; }

		/// <summary>
		/// Defines whether the settings could be retrieved and parsed successfully.
		/// </summary>
		public bool Success { get; set; }
	}
}
