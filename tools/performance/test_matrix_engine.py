import asyncio
import unittest
from matrix_engine import BoundedLines, demultiplex


class AttachTests(unittest.IsolatedAsyncioTestCase):
    async def decode(self, data):
        reader, output = asyncio.StreamReader(), BoundedLines()
        reader.feed_data(data)
        reader.feed_eof()
        await demultiplex(reader, output)
        return await output.readline()

    async def test_stdout_and_stderr_are_separate(self):
        self.assertEqual(b'ok\n', await self.decode(b'\x02\0\0\0\0\0\0\x04bad\n\x01\0\0\0\0\0\0\x03ok\n'))

    async def test_split_output_frames_reassemble_line(self):
        self.assertEqual(b'ok\n', await self.decode(b'\x01\0\0\0\0\0\0\x01o\x01\0\0\0\0\0\0\x02k\n'))

    async def test_rejects_invalid_stream(self):
        with self.assertRaises(ValueError):
            await self.decode(b'\x03\0\0\0\0\0\0\0')

    async def test_rejects_huge_length_without_allocating(self):
        with self.assertRaises(ValueError):
            await self.decode(b'\x01\0\0\0\xff\xff\xff\xff')

    async def test_rejects_truncated_data(self):
        with self.assertRaises(ValueError):
            await self.decode(b'\x01\0\0\0\0\0\0\x04ab')

    async def test_rejects_unconsumed_flood(self):
        output = BoundedLines()
        output.append(b'x' * 1048576)
        with self.assertRaises(ValueError):
            output.append(b'x')

    async def test_consumption_releases_budget(self):
        output = BoundedLines()
        output.append(b'a\n')
        self.assertEqual(b'a\n', await output.readline())
        self.assertEqual(0, output.pending)


if __name__ == '__main__':
    unittest.main()
