import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import wave

project = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("local_diarize", project / "scripts" / "diarize.py")
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)
from pyannote.audio import Pipeline


class FakeAnnotation:
    def itertracks(self, yield_label=False):
        from types import SimpleNamespace
        for start, end, speaker in [(0, 1, "A"), (1, 2, "B"), (2, 3, "A")]:
            yield SimpleNamespace(start=start, end=end), "track", speaker


class FakePipeline:
    def to(self, device):
        return self

    def __call__(self, audio, min_speakers, max_speakers):
        from types import SimpleNamespace
        assert audio["waveform"].shape == (1, 48000)
        assert audio["sample_rate"] == 16000
        assert min_speakers == 1 and max_speakers == 20
        return SimpleNamespace(exclusive_speaker_diarization=FakeAnnotation())


class BridgeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(dir=project / ".cli")
        self.root = Path(self.temporary.name)
        (self.root / "config.yaml").write_text("test", encoding="utf-8")
        self.audio = self.root / "sample.wav"
        with wave.open(str(self.audio), "wb") as output:
            output.setnchannels(1)
            output.setsampwidth(2)
            output.setframerate(16000)
            output.writeframes(bytes(96000))

    def tearDown(self):
        assert self.root.resolve().is_relative_to((project / ".cli").resolve())
        self.temporary.cleanup()

    def run_bridge(self, *extra):
        arguments = ["diarize.py", "--model", str(self.root), *extra]
        with patch.object(sys, "argv", arguments):
            return bridge.main()

    def test_global_labels_and_waveform_input(self):
        destination = self.root / "speakers.json"
        with patch.object(Pipeline, "from_pretrained", return_value=FakePipeline()):
            self.assertEqual(self.run_bridge("--audio", str(self.audio), "--output", str(destination)), 0)
        data = json.loads(destination.read_text(encoding="utf-8"))
        self.assertEqual([turn["speaker"] for turn in data], ["Hablante 1", "Hablante 2", "Hablante 1"])
        self.assertEqual(data[-1]["endSeconds"], 3)
        self.assertFalse(destination.with_suffix(".tmp").exists())

    def test_model_failure_is_controlled(self):
        with patch.object(Pipeline, "from_pretrained", side_effect=RuntimeError("private failure")):
            self.assertEqual(self.run_bridge("--check"), 11)

    def test_invalid_pcm_is_rejected(self):
        with wave.open(str(self.audio), "wb") as output:
            output.setnchannels(2)
            output.setsampwidth(2)
            output.setframerate(16000)
            output.writeframes(bytes(96000))
        with patch.object(Pipeline, "from_pretrained", return_value=FakePipeline()):
            self.assertEqual(self.run_bridge("--audio", str(self.audio), "--output", str(self.root / "bad.json")), 12)


if __name__ == "__main__":
    unittest.main()
