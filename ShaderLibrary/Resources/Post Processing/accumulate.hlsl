#define LANG_CUDA
texture<float, 2, cudaReadModeElementType> depthTex;


__inline__ __device__ bool operator==(const float2 a, const float2 b) {
	//return fabs(a.x - b.x) < 1e-6f && fabs(a.y - b.y) < 1e-6f;
	return a.x == b.x && a.y == b.y;
}
__inline__ __device__ bool operator!=(const float2 a, const float2 b) {
	//return fabs(a.x - b.x) > 1e-6f || fabs(a.y - b.y) > 1e-6f;
	return a.x != b.x || a.y != b.y;
}
__inline__ __device__ float2 operator+(const float2 a, const float2 b) {
	return make_float2(a.x+b.x, a.y+b.y);
}

__inline__ __device__ float2 operator-(const float2 a, const float2 b) {
	return make_float2(a.x-b.x, a.y-b.y);
}

__inline__ __device__ float2 operator-(const float2 a) {
	return make_float2(-a.x, -a.y);
}

__inline__ __device__ void operator*=(float2 &a, const float c) {
	a.x *= c;
	a.y *= c;
}
__inline__ __device__ void operator*=(float2 &a, const float2 c) {
	a.x *= c.x;
	a.y *= c.y;
}

__inline__ __device__ float2 operator*(const float2 a, const float c) {
	return make_float2(a.x*c, a.y*c);
}

__inline__ __device__ float2 operator*(const float c, const float2 a) {
	return make_float2(a.x*c, a.y*c);
}

__inline__ __device__ void operator+=(float2 &a, const float2 b) {
	a.x += b.x;
	a.y += b.y;
}

__inline__ __device__ void operator-=(float2 &a, const float2 b) {
	a.x -= b.x;
	a.y -= b.y;
}

extern "C" __global__ void accumulate(const float2 * __restrict sweepData, const uint * __restrict indexData, float * __restrict out) {
  int tidY = blockIdx.y*blockDim.y + threadIdx.y;
  int tidX = blockIdx.x*blockDim.x + threadIdx.x;
  if (tidY >= 720 || tidX >= 1280) return;
  float occlusion = 0.0f;
  int thisIndex = tidY*1280 + tidX;
  float2 myCoord = make_float2(1.285000E+02f + (float)tidX, 7.915000E+02f - (float)tidY);
  myCoord *= make_float2(6.510417E-04f, 1.157407E-03f);
  float myHeightI = (1.0f/(tex2D(depthTex, myCoord.x, myCoord.y)));
  float accepted = 0.0f;
  for (int line = 0; line < 8; ++line) {
    int finalIndex = thisIndex + line*921600;
    unsigned int lineIndex = indexData[finalIndex];
    float2 sweepSample = sweepData[lineIndex];
    float thisDiff = fabsf(1.0f - sweepSample.x*myHeightI);
    if (thisDiff < 6.999969E-03f) {
      unsigned int occData = *((unsigned int*)(&sweepSample.y));
      float tempOcc = __half2float(occData&0xffff) + __half2float(occData>>16);
      occlusion += fminf(2.0f, fmaxf(0.0f, tempOcc));
      accepted += 2.0f;
    }
  }
  occlusion /= accepted;
  out[tidY*1536 + tidX + 110720] = 1.0f - occlusion;
}
