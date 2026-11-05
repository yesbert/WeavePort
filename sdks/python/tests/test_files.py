"""Local validation of function file names, media types and block sizes."""

import unittest

from weaveport_sdk.files import FileSession, PluginFileError, validate_block_bytes


class FileValidationTests(unittest.TestCase):
    def session(self, outputs=True):
        files = dict(inputs=[dict(name="sheet", length=10, mediaType="text/plain")])
        if outputs:
            files["outputs"] = dict(maximumCount=2, maximumBytes=100)
        return FileSession(dict(files=files), None, 65536)

    def test_names_reject_trailing_newline_and_paths(self):
        session = self.session()
        for name in ("result.xlsx\n", ".hidden", "a/b", "", "x" * 129):
            with self.assertRaises(ValueError):
                session.reserve_output(name, "text/plain")
        with self.assertRaises(ValueError):
            session.reserve_output("ok", "text/plain\n")
        session.reserve_output("result.xlsx", "application/zip")

    def test_refusals_are_local(self):
        self.assertEqual(self.session().input("sheet").length, 10)
        with self.assertRaises(PluginFileError) as missing:
            self.session().input("nope")
        self.assertEqual(missing.exception.code, "input-not-found")
        with self.assertRaises(PluginFileError) as denied:
            self.session(outputs=False).reserve_output("a", "text/plain")
        self.assertEqual(denied.exception.code, "output-not-permitted")
        session = self.session()
        session.reserve_output("a", "text/plain")
        with self.assertRaises(PluginFileError) as twice:
            session.reserve_output("a", "text/plain")
        self.assertEqual(twice.exception.code, "output-exists")

    def test_block_size_bounds(self):
        self.assertEqual(validate_block_bytes(4096), 4096)
        for value in (4095, 262145, "65536"):
            with self.assertRaises(ValueError):
                validate_block_bytes(value)


if __name__ == "__main__":
    unittest.main()
