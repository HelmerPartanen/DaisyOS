# Dept Effect models

These files ship with the shell and are copied to its build/publish output. There are no runtime downloads, Python dependency, network calls or external inference processes. CPU ONNX Runtime is the only added runtime dependency.

- `u2netp.onnx`: U²-Net small general foreground model, from [rembg v0.0.0](https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx). Original research/code: [U-2-Net](https://github.com/xuebinqin/U-2-Net), Xuebin Qin et al. Apache-2.0; see `LICENSE`. SHA-256: `309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8`.
- `skyseg.onnx`: sky segmentation from [Sky-Segmentation-and-Post-processing](https://github.com/xiongzhu666/Sky-Segmentation-and-Post-processing), distributed by [JianyuanWang/skyseg](https://huggingface.co/JianyuanWang/skyseg/tree/3ba8c6df1d9ba9ff26f637c7ba9568ac11a9aa7f). MIT; see `SKY-LICENSE`. We converted its weights to float16, preserving float32 inputs/outputs. This halves the original 168 MiB model to approximately 85 MiB. SHA-256 of the shipped model: `74d87f4a69378a610a6be662f859c38cfbdfdd75ff74bbfc54842965ed6fc9f7`.

Reproduce the sky model using a development virtual environment with `onnx==1.23.2` and `onnxconverter-common==1.16.0`:

```python
from urllib.request import urlopen
from hashlib import sha256
import onnx
from onnxconverter_common import float16

url = 'https://huggingface.co/JianyuanWang/skyseg/resolve/3ba8c6df1d9ba9ff26f637c7ba9568ac11a9aa7f/skyseg.onnx'
source = urlopen(url).read()
assert sha256(source).hexdigest() == 'ab9c34c64c3d821220a2886a4a06da4642ffa14d5b30e8d5339056a089aa1d39'
model = onnx.load_model_from_string(source)
model = float16.convert_float_to_float16(model, keep_io_types=True)
onnx.save(model, 'skyseg.onnx')
```

Both models take RGB NCHW `[1,3,320,320]` with ImageNet channel standardization. U²-Net follows the upstream maximum-channel-value normalization; the sky model uses `/255`. The first output is the fused foreground/sky probability respectively. Sky output is inverted. Uncertain, near-uniform, badly bounded or visually unsupported silhouettes are rejected. Colour-guided upsampling refines accepted masks to 640×640; the original wallpaper supplies all foreground colour pixels.

Model fingerprints participate in the cache key, so replacing weights automatically invalidates prior segmentation results. Missing/corrupt models or unavailable native ONNX Runtime cause ordinary lock-screen rendering.
