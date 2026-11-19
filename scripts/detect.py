# Megabat Detect: Grounding DINO on rendered views
# usage: python detect.py job.json result.json
# job.json: {"images": [path, ...], "prompt": "radiator. toilet.", "score": 0.35, "text": 0.25}
# result.json: per image a list of {"box": [x0, y0, x1, y1], "score": s, "label": "radiator"}
import sys, json, time
import torch
from PIL import Image
from transformers import AutoProcessor, AutoModelForZeroShotObjectDetection

MODEL = "IDEA-Research/grounding-dino-base"

def main(job_path, out_path):
    t0 = time.time()
    job = json.load(open(job_path, encoding="utf-8"))
    prompt = job["prompt"].strip().lower()
    if not prompt.endswith("."):
        prompt += "."
    dev = "cuda" if torch.cuda.is_available() else "cpu"
    proc = AutoProcessor.from_pretrained(MODEL)
    model = AutoModelForZeroShotObjectDetection.from_pretrained(MODEL).to(dev).eval()
    t1 = time.time()
    res = []
    for path in job["images"]:
        im = Image.open(path).convert("RGB")
        inp = proc(images=im, text=prompt, return_tensors="pt").to(dev)
        with torch.no_grad():
            o = model(**inp)
        r = proc.post_process_grounded_object_detection(
            o, inp.input_ids, threshold=job.get("score", 0.35), text_threshold=job.get("text", 0.25),
            target_sizes=[im.size[::-1]])[0]
        labels = r.get("text_labels", r.get("labels"))
        res.append([{"box": [float(v) for v in b], "score": float(s), "label": str(l)}
                    for b, s, l in zip(r["boxes"].tolist(), r["scores"].tolist(), labels)])
    json.dump({"device": dev, "load_s": t1 - t0, "detect_s": time.time() - t1, "views": res},
              open(out_path, "w", encoding="utf-8"))

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
