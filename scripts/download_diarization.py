"""One-time model download; HF_TOKEN is consumed from the environment, never saved."""
import os
from pathlib import Path
import subprocess
import sys

os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
os.environ["PYANNOTE_METRICS_ENABLED"] = "0"
cache = Path(__file__).resolve().parent.parent / "tools" / "diarization" / "cache"
os.environ.setdefault("HF_HOME", str(cache / "huggingface"))
os.environ.pop("HF_HUB_OFFLINE", None)


def main():
    if len(sys.argv) != 2 or not os.environ.get("HF_TOKEN"):
        print("Se requiere un token de lectura de Hugging Face para descargar Community-1.")
        return 1
    model = Path(sys.argv[1]).resolve()
    model.mkdir(parents=True, exist_ok=True)
    marker = model / ".ready"
    marker.unlink(missing_ok=True)
    try:
        from huggingface_hub import snapshot_download
        snapshot_download("pyannote/speaker-diarization-community-1", local_dir=str(model), token=os.environ["HF_TOKEN"])
        environment = dict(os.environ)
        environment.pop("HF_TOKEN", None)
        environment["HF_HUB_OFFLINE"] = "1"
        check = subprocess.run([sys.executable, str(Path(__file__).with_name("diarize.py")), "--model", str(model), "--check"],
                               env=environment, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if check.returncode:
            print("El modelo no pasó la comprobación offline. Revisa la instalación de pyannote.audio y los archivos descargados.")
            return 1
        marker.write_text("Community-1 validated offline\n", encoding="utf-8")
        print("Modelo Community-1 preparado y validado para uso local sin token.")
        return 0
    except Exception:
        print("No se pudo descargar el modelo. Comprueba conexión, token de lectura y aceptación de condiciones de Community-1.")
        return 1


if __name__ == "__main__":
    sys.exit(main())
