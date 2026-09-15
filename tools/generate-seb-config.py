#!/usr/bin/env python3
"""
Generator for an unencrypted SEB ("plnd") configuration file for the CBT integration.

The resulting .seb file uses the standard SEB binary container format:

    gzip( "plnd" + gzip( <plist XML> ) )

This matches what SafeExamBrowser.Configuration.DataFormats.BinarySerializer produces for a plain
data block (no password / no certificate), and what BinaryParser expects when loading a .seb file.

Usage:
    python3 tools/generate-seb-config.py [output.seb]

The generated configuration locks the exam client to https://cbt.smkdata.sch.id and instructs it to
retrieve the quit/unlock password from https://cbt.smkdata.sch.id/api/kiosk/settings (see the
cbtKioskURL / cbtKioskTimeout settings), so the exam administrator can manage the quit password
centrally via the CBT panel.
"""

import gzip
import io
import os
import sys
from xml.sax.saxutils import escape

CBT_BASE_URL = "https://cbt.smkdata.sch.id"
CBT_KIOSK_URL = "https://cbt.smkdata.sch.id/api/kiosk/settings"
CBT_KIOSK_TIMEOUT = 5000


# --- Value constructors, mirroring the SEB plist data model ------------------

def string(value):
    return ("string", value)


def integer(value):
    return ("integer", value)


def boolean(value):
    return ("true", None) if value else ("false", None)


def array(items):
    return ("array", items)


def dictionary(items):
    return ("dict", items)


def _write_value(out, kind, value):
    if kind == "string":
        out.write("<string>{}</string>\n".format(escape(value or "")))
    elif kind == "integer":
        out.write("<integer>{}</integer>\n".format(value))
    elif kind in ("true", "false"):
        out.write("<{} />\n".format(kind))
    elif kind == "array":
        out.write("<array>\n")
        for item_kind, item_value in value:
            _write_value(out, item_kind, item_value)
        out.write("</array>\n")
    elif kind == "dict":
        out.write("<dict>\n")
        # SEB's XmlSerializer orders keys alphabetically -> do the same.
        for key, (item_kind, item_value) in sorted(value.items()):
            out.write("<key>{}</key>\n".format(escape(key)))
            _write_value(out, item_kind, item_value)
        out.write("</dict>\n")
    else:
        raise ValueError("Unsupported value kind: {}".format(kind))


# --- Configuration -----------------------------------------------------------

def build_settings():
    """Returns the settings dictionary (key -> (kind, value))."""
    host = CBT_BASE_URL.split("://", 1)[-1]

    filter_rules = [
        dictionary({
            "action": integer(1),            # 1 = allow
            "active": boolean(True),
            "expression": string(host),
            "regex": boolean(False),
        }),
        dictionary({
            "action": integer(1),
            "active": boolean(True),
            "expression": string(host + "/*"),
            "regex": boolean(False),
        }),
    ]

    return {
        # --- Exam start page -------------------------------------------------
        "startURL": string(CBT_BASE_URL),

        # --- CBT integration (quit/unlock password managed via the CBT panel) -
        "cbtKioskURL": string(CBT_KIOSK_URL),
        "cbtKioskTimeout": integer(CBT_KIOSK_TIMEOUT),

        # --- Quit behaviour --------------------------------------------------
        # The quit password is NOT stored here; it is retrieved from cbtKioskURL at runtime.
        "allowQuit": boolean(True),
        "quitURLConfirm": boolean(True),

        # --- Browser / kiosk lockdown ---------------------------------------
        "browserViewMode": integer(1),            # 1 = full screen
        "enableSebBrowser": boolean(True),
        "showTaskBar": boolean(True),
        "showTime": boolean(True),
        "allowBrowsingBackForward": boolean(False),
        "browserWindowAllowAddressBar": boolean(False),
        "enableDeveloperConsole": boolean(False),
        "allowDownloads": boolean(False),
        "allowUploads": boolean(False),
        "allowPrint": boolean(False),
        "enablePrintScreen": boolean(False),
        "allowScreenSharing": boolean(False),
        "clipboardPolicy": integer(1),            # 1 = block clipboard

        # --- Keyboard --------------------------------------------------------
        "enableAltTab": boolean(False),
        "enableAltF4": boolean(False),
        "enableAltEsc": boolean(False),
        "enableCtrlEsc": boolean(False),
        "enableStartMenu": boolean(False),
        "enableF12": boolean(False),
        "enableRightMouse": boolean(False),

        # --- Kiosk mode ------------------------------------------------------
        "createNewDesktop": boolean(True),
        "killExplorerShell": boolean(False),

        # --- URL filter: only allow the CBT host -----------------------------
        "URLFilterEnable": boolean(True),
        "URLFilterEnableContentFilter": boolean(True),
        "URLFilterRules": array(filter_rules),

        # --- Security --------------------------------------------------------
        "allowApplicationLog": boolean(False),
        "allowVirtualMachine": boolean(False),
        "sebConfigPurpose": integer(0),           # 0 = starting an exam
    }


def build_plist(settings):
    out = io.StringIO()
    out.write('<?xml version="1.0" encoding="UTF-8"?>\n')
    out.write('<!DOCTYPE plist PUBLIC "-//Apple Computer//DTD PLIST 1.0//EN" '
              '"https://www.apple.com/DTDs/PropertyList-1.0.dtd">\n')
    out.write('<plist version="1.0">\n')
    _write_value(out, "dict", settings)
    out.write('</plist>\n')
    return out.getvalue()


def build_seb(plist_xml):
    inner = gzip.compress(plist_xml.encode("utf-8"), mtime=0)
    payload = b"plnd" + inner
    return gzip.compress(payload, mtime=0)


def main():
    output = sys.argv[1] if len(sys.argv) > 1 else "examples/cbt-exam-config.seb"
    os.makedirs(os.path.dirname(output) or ".", exist_ok=True)

    settings = build_settings()
    plist_xml = build_plist(settings)
    seb = build_seb(plist_xml)

    with open(output, "wb") as handle:
        handle.write(seb)

    # Self-verification: re-parse the generated container.
    assert gzip.decompress(seb)[:4] == b"plnd", "outer container prefix mismatch"
    assert gzip.decompress(gzip.decompress(seb)[4:]).lstrip().startswith(b"<?xml"), "inner XML mismatch"

    print("Wrote {} ({} bytes)".format(output, len(seb)))
    print("Start URL : {}".format(CBT_BASE_URL))
    print("Kiosk API : {}".format(CBT_KIOSK_URL))


if __name__ == "__main__":
    main()
