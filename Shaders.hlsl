cbuffer SceneConstantBuffer : register(b0)
{
    float4 right;   // .w unused
    float4 forward; // .w unused
    float4 up;      // .w unused
    float4 origin;  // .w unused
    float len;
    float x;
    float y;
    float padding;  // Total 4*16 + 16 = 80 bytes.
};

struct PSInput
{
    float4 position : SV_POSITION;
    float3 dir : TEXCOORD0;
    float3 localdir : TEXCOORD1;
};

#define PI 3.14159265358979324
#define M_L 0.3819660113
#define M_R 0.6180339887
#define MAXR 8
#define SOLVER 8

// Original KERNEL function ported to HLSL
float kernal(float3 ver)
{
   float3 a;
   float b,c,d,e;
   a = ver;
   
   [unroll] // Optimization hint
   for(int i=0; i<5; i++){
       b = length(a);
       c = atan2(a.y, a.x) * 8.0;
       e = 1.0/b;
       d = acos(a.z/b) * 8.0;
       b = pow(abs(b), 8.0); // pow in HLSL might NaN with negative, usually length is positive though.
       a = float3(b*sin(d)*cos(c), b*sin(d)*sin(c), b*cos(d)) + ver;
       if(b > 6.0){
           break;
       }
   }
   return 4.0 - a.x*a.x - a.y*a.y - a.z*a.z;
}

PSInput VSMain(float3 position : POSITION, float2 texCoord : TEXCOORD)
{
    PSInput result;
    result.position = float4(position, 1.0);
    
    // original: dir = forward + right * position.x*x + up * position.y*y;
    // Note: GLSL position is attribute vec4, but provided as float3 usually.
    // The "x" and "y" uniforms are aspect/scale factors.
    // The position.x and position.y are the vertex coordinates (-1 to 1).
    
    // Careful with "x" and "y" names vs swizzles.
    // The cbuffer has x and y floats.
    // Let's use position.x and position.y from input.
    
    result.dir = forward.xyz + right.xyz * (position.x * x) + up.xyz * (position.y * y);
    result.localdir = float3(position.x * x, position.y * y, -1.0);
    
    return result;
}

float4 PSMain(PSInput input) : SV_TARGET
{
    float3 dir = input.dir;
    // localdir seems unused in the original FSHADER logic?
    // Wait, original FSHADER has: varying vec3 dir, localdir;
    // But localdir is referenced in the reflection calculation?
    // "ver = localdir;" inside the sign==1 block. Yes.
    
    float3 localdir = input.localdir;
    
    // Uniforms used: origin (.xyz), len
    float3 origin_vec = origin.xyz;
    
    float3 color = float3(0.0, 0.0, 0.0);
    int sign_flag = 0;
    
    float v, v1, v2;
    float r1, r2, r3, r4, m1, m2, m3, m4;
    float3 n, reflect_vec;
    const float step_val = 0.002; // renamed step -> step_val (step is intrinsic)
    
    v1 = kernal(origin_vec + dir * (step_val * len));
    v2 = kernal(origin_vec);
    
    // Loop
    // HLSL doesn't like massive loops without unroll/loop attributes sometimes, but 1000 is okay.
    [loop]
    for (int k = 2; k < 1002; k++) {
        float3 ver = origin_vec + dir * (step_val * len * float(k));
        v = kernal(ver);
        
        if (v > 0.0 && v1 < 0.0) {
             r1 = step_val * len * float(k - 1);
             r2 = step_val * len * float(k);
             m1 = kernal(origin_vec + dir * r1);
             m2 = kernal(origin_vec + dir * r2);
             
             for (int l = 0; l < SOLVER; l++) {
                r3 = r1 * 0.5 + r2 * 0.5;
                m3 = kernal(origin_vec + dir * r3);
                if (m3 > 0.0) {
                   r2 = r3;
                   m2 = m3;
                }
                else {
                   r1 = r3;
                   m1 = m3;
                }
             }
             
             if (r3 < 2.0 * len) {
                   sign_flag = 1;
                   break;
             }
        }
        
        if (v < v1 && v1 > v2 && v1 < 0.0 && (v1 * 2.0 > v || v1 * 2.0 > v2)) {
             r1 = step_val * len * float(k - 2);
             r2 = step_val * len * (float(k) - 2.0 + 2.0 * M_L);
             r3 = step_val * len * (float(k) - 2.0 + 2.0 * M_R);
             r4 = step_val * len * float(k);
             
             m2 = kernal(origin_vec + dir * r2);
             m3 = kernal(origin_vec + dir * r3);
             
             for (int l = 0; l < MAXR; l++) {
                if (m2 > m3) {
                   r4 = r3;
                   r3 = r2;
                   r2 = r4 * M_L + r1 * M_R;
                   m3 = m2;
                   m2 = kernal(origin_vec + dir * r2);
                }
                else {
                   r1 = r2;
                   r2 = r3;
                   r3 = r4 * M_R + r1 * M_L;
                   m2 = m3;
                   m3 = kernal(origin_vec + dir * r3);
                }
             }
             
             if (m2 > 0.0) {
                r1 = step_val * len * float(k - 2);
                // r2 = r2;
                m1 = kernal(origin_vec + dir * r1);
                m2 = kernal(origin_vec + dir * r2);
                for (int l = 0; l < SOLVER; l++) {
                   r3 = r1 * 0.5 + r2 * 0.5;
                   m3 = kernal(origin_vec + dir * r3);
                   if (m3 > 0.0) {
                      r2 = r3;
                      m2 = m3;
                   }
                   else {
                      r1 = r3;
                      m1 = m3;
                   }
                }
                if (r3 < 2.0 * len && r3 > step_val * len) {
                       sign_flag = 1;
                       break;
                }
             }
             else if (m3 > 0.0) {
                r1 = step_val * len * float(k - 2);
                r2 = r3;
                m1 = kernal(origin_vec + dir * r1);
                m2 = kernal(origin_vec + dir * r2);
                for (int l = 0; l < SOLVER; l++) {
                   r3 = r1 * 0.5 + r2 * 0.5;
                   m3 = kernal(origin_vec + dir * r3);
                   if (m3 > 0.0) {
                      r2 = r3;
                      m2 = m3;
                   }
                   else {
                      r1 = r3;
                      m1 = m3;
                   }
                }
                if (r3 < 2.0 * len && r3 > step_val * len) {
                       sign_flag = 1;
                       break;
                }
             }
        }
        
        v2 = v1;
        v1 = v;
    }
    
    if (sign_flag == 1) {
          float3 ver = origin_vec + dir * r3;
          r1 = ver.x * ver.x + ver.y * ver.y + ver.z * ver.z;
          
          n.x = kernal(ver - right.xyz * (r3 * 0.00025)) - kernal(ver + right.xyz * (r3 * 0.00025));
          n.y = kernal(ver - up.xyz * (r3 * 0.00025)) - kernal(ver + up.xyz * (r3 * 0.00025));
          n.z = kernal(ver + forward.xyz * (r3 * 0.00025)) - kernal(ver - forward.xyz * (r3 * 0.00025));
          
          r3 = n.x * n.x + n.y * n.y + n.z * n.z;
          n = n * (1.0 / sqrt(r3));
          
          ver = localdir;
          r3 = ver.x * ver.x + ver.y * ver.y + ver.z * ver.z;
          ver = ver * (1.0 / sqrt(r3));
          
          reflect_vec = n * (-2.0 * dot(ver, n)) + ver;
          
          r3 = reflect_vec.x * 0.276 + reflect_vec.y * 0.920 + reflect_vec.z * 0.276;
          r4 = n.x * 0.276 + n.y * 0.920 + n.z * 0.276;
          
          r3 = max(0.0, r3);
          r3 = r3 * r3 * r3 * r3;
          r3 = r3 * 0.45 + r4 * 0.25 + 0.3;
          
          n.x = sin(r1 * 10.0) * 0.5 + 0.5;
          n.y = sin(r1 * 10.0 + 2.05) * 0.5 + 0.5;
          n.z = sin(r1 * 10.0 - 2.05) * 0.5 + 0.5;
          
          color = n * r3;
    }

    return float4(color, 1.0);
}
