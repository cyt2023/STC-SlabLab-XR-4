"""Cloud and local speech transcription."""

from __future__ import annotations

import os
import tempfile
from pathlib import Path

from fastapi import APIRouter, File, HTTPException, UploadFile

from .. import state

router = APIRouter()

def _transcribe_locally(audio: bytes, suffix: str) -> tuple[str, str]:
    """Run a CPU Whisper fallback when the configured cloud service is unavailable."""
    try:
        from faster_whisper import WhisperModel
    except ImportError as exc:
        raise RuntimeError(
            "Local speech recognition is not installed (pip install faster-whisper)."
        ) from exc

    with state._voice_model_lock:
        if state._voice_model is None:
            model_name = os.getenv("VOICE_LOCAL_MODEL", "base")
            state._voice_model = WhisperModel(model_name, device="cpu", compute_type="int8")
        model = state._voice_model

    temp_path = ""
    try:
        with tempfile.NamedTemporaryFile(delete=False, suffix=suffix or ".wav") as temp_audio:
            temp_audio.write(audio)
            temp_path = temp_audio.name
        segments, _ = model.transcribe(
            temp_path,
            beam_size=5,
            vad_filter=True,
            condition_on_previous_text=False,
        )
        text = " ".join(segment.text.strip() for segment in segments).strip()
        return text, f"faster-whisper/{os.getenv('VOICE_LOCAL_MODEL', 'base')}"
    finally:
        if temp_path:
            try:
                os.unlink(temp_path)
            except OSError:
                pass

@router.post("/speech/transcribe")
async def transcribe_speech(file: UploadFile = File(...)) -> dict[str, str]:
    """Transcribe a short Quest microphone recording.

    Quest headsets do not consistently ship an Android RecognitionService, so
    the XR client records locally and sends a WAV clip here.  Speech is kept
    separate from MatPlot generation and returns only editable task text.
    """
    audio = await file.read()
    if len(audio) < 64:
        raise HTTPException(status_code=400, detail="The audio recording is empty.")
    if len(audio) > 12 * 1024 * 1024:
        raise HTTPException(status_code=413, detail="The audio recording is too large.")

    key = os.getenv("VOICE_OPENAI_API_KEY") or os.getenv("OPENAI_API_KEY")
    base_url = os.getenv("VOICE_OPENAI_BASE_URL", "https://api.openai.com/v1")
    model = os.getenv("VOICE_TRANSCRIBE_MODEL", "gpt-4o-mini-transcribe")
    text = ""
    used_model = model
    cloud_error = ""
    if key and os.getenv("VOICE_LOCAL_FIRST", "0") != "1":
        try:
            from openai import OpenAI

            client = OpenAI(api_key=key, base_url=base_url)
            result = client.audio.transcriptions.create(
                model=model,
                file=(file.filename or "quest-voice.wav", audio, file.content_type or "audio/wav"),
            )
            text = str(getattr(result, "text", "") or "").strip()
        except Exception as exc:
            cloud_error = str(exc)[:240]

    if not text:
        try:
            suffix = Path(file.filename or "quest-voice.wav").suffix or ".wav"
            text, used_model = _transcribe_locally(audio, suffix)
        except Exception as exc:
            detail = f"Local speech recognition failed: {str(exc)[:220]}"
            if cloud_error:
                detail += f" Cloud service also failed: {cloud_error}"
            raise HTTPException(status_code=502, detail=detail) from exc
    if not text:
        raise HTTPException(status_code=422, detail="No speech was recognized.")
    return {"text": text, "model": used_model}
