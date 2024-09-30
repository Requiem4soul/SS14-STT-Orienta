import asyncio
import websockets
import json
import io
from faster_whisper import WhisperModel
import soundfile as sf

# Инициализация модели
print("Начало загрузки модели, это займёт время при первом запуске")
model_size = "large-v3"
model = WhisperModel(model_size, device="cuda", compute_type="float16")
print(f"Модель {model_size} была успешно загружена")

# Очередь для обработки аудио (асинхронная (это важно))
audio_queue = asyncio.Queue()


async def handle_client(websocket, path):
    try:
        async for message in websocket:
            print(f"Запись с клиента была получена")
            # Добавляем запрос в асинхронную очередь
            await audio_queue.put((websocket, message))
    # Ошибка 1001 (no close frame received or sent) - это не критическая ошибка. Она означает что клиент отключился от сервера
    except Exception as e:
        print(f"Произошла ошибка: {e}")

async def process_audio_queue():
    while True:
        websocket, audio_data = await audio_queue.get()
        # Обрабатываем каждый запрос в отдельной асинхронной задаче (важно)
        asyncio.create_task(process_audio(websocket, audio_data))

async def process_audio(websocket, audio_data):
    try:
        # WAV файл в бинарном формате (поддерживается сокетами по умолчанию)
        wav_bytes = io.BytesIO(audio_data)
        audio_data, sample_rate = sf.read(wav_bytes)
        
        # STT (аудио обрабатывается в распознанный текст)
        segments, _ = model.transcribe(audio_data, beam_size=5, language="ru")
        
        # Собираем текст (сегменты указывают не весь текст в аудио, а только в некотором промежутке времени. Для получения полной расщифровки их необходимо объединять)
        transcription = "".join([segment.text for segment in segments])
        
        # Можно использовать для поиска и удаления определённых фраз. Так как STT прибавляет пробелы, то будет очень нужным
        #print(f'Расшифровка. Длина {len(transcription)}. Текст:"{transcription}"')
        
        # " Продолжение следует..." и другие "приколы" от Fast Whisper при пустом аудио (без слов)
        if transcription == " Продолжение следует..." or transcription == " Субтитры создавал DimaTorzok" or transcription == " Субтитры сделал DimaTorzok":
            transcription = ""
        
        # Отправляем обратно клиенту (поддерживается в сокетах по умолчанию)
        await websocket.send(transcription)
        print(f"Клиент получил расшифроку: {transcription}")

    except Exception as e:
        print(f"Произошла ошибка: {e}")

async def start_server():
    # Запуск обработки очереди
    asyncio.create_task(process_audio_queue())

    # Запуск сервера (меняйте порт лок хоста, если у вас должно быть подключение к другому порту, но ознакомьтесь с инструкцией портов и не подключитесь к служебному). Это может повлечь к критическому сбою
    # По умолчанию, для тестирования, тут указан порт 5000. Меняйте в следуйщей строке (58) порт вместо 5000
    async with websockets.serve(handle_client, 'localhost', 5000, max_size=10**7, ping_interval=None):
        print("Сервер начал свою работу")
        await asyncio.Future()  # Ожидание для работы сервера

if __name__ == "__main__":
    asyncio.run(start_server())
