#ifndef B2U_CONSTANTS_INCLUDED
#define B2U_CONSTANTS_INCLUDED

// Schema defined min/max values for each workflow's properties. The editor can write to this file to modify the min/max if the schema changes. 
// It can ONLY be changed in editor as changing these values requires the shaders to recompile.
// Structure:
// ///<summary>
// ///{uiName} ({Note if applicable})
// ///<summary>
// static const half2 {Workflow}_{slotID}_MinMax = half2(Min, Max);

// --- OPAQUE/OPAQUE_DS RANGES ---
/// <summary>
/// Metallic
/// </summary>
static const half2 Opaque_r1_ca_MinMax = half2(0.0, 1.0);

/// <summary>
/// Smoothness
/// </summary>
static const half2 Opaque_r1_ea_MinMax = half2(0.0, 1.0);

/// <summary>
/// Emission Strength
/// </summary>
static const half2 Opaque_r1_p1_r_MinMax = half2(0.0, 5.0);

/// <summary>
/// Emission Speed
/// </summary>
static const half2 Opaque_r1_p1_g_MinMax = half2(0.0, 5.0);

/// <summary>
/// Iridescence Srength
/// </summary>
static const half2 Opaque_r1_p1_b_MinMax = half2(0.0, 3.0);

/// <summary>
/// Iridescence Power
/// </summary>
static const half2 Opaque_r1_p1_a_MinMax = half2(0.0, 5.0);

/// <summary>
/// Detail Gradient Power
/// </summary>
static const half2 Opaque_r1_p2_r_MinMax = half2(0.0, 10.0);

/// <summary>
/// Detail Normal Strength
/// </summary>
static const half2 Opaque_r1_p2_g_MinMax = half2(0.0, 2.0);

/// <summary>
/// Detail Curvature Strength
/// </summary>
static const half2 Opaque_r1_p2_b_MinMax = half2(0.0, 2.0);

/// <summary>
/// Detail Booleans (Bitmask Slot. Use Decode_Bitmask instead of UnpackLUT)
/// </summary>
static const half2 Opaque_r1_p2_a_MinMax = half2(0.0, 255.0);

/// <summary>
/// Backface Color Multiplier
/// </summary>
static const half2 Opaque_r1_p3_r_MinMax = half2(-1.0, 1.0);

/// <summary>
/// Backface Booleans (Bitmask Slot. Use Decode_Bitmask instead of UnpackLUT)
/// </summary>
static const half2 Opaque_r1_p3_g_MinMax = half2(0.0, 255.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Opaque_r1_p3_b_MinMax = half2(0.0, 0.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Opaque_r1_p3_a_MinMax = half2(0.0, 0.0);

/// <summary>
/// Detail Index
/// </summary>
static const half2 Opaque_r1_p4_g_MinMax = half2(0.0, 64.0);

/// <summary>
/// Emission Mode
/// </summary>
static const half2 Opaque_r1_p4_b_MinMax = half2(0.0, 7.0);

/// <summary>
/// Iridescence Mode
/// </summary>
static const half2 Opaque_r1_p4_a_MinMax = half2(0.0, 8.0);




// --- TRANSPARENT RANGES ---

/// <summary>
/// Metallic
/// </summary>
static const half2 Transparent_r1_ca_MinMax = half2(0.0, 1.0);

/// <summary>
/// Smoothness
/// </summary>
static const half2 Transparent_r1_ea_MinMax = half2(0.0, 1.0);

/// <summary>
/// Emission Strength
/// </summary>
static const half2 Transparent_r1_p1_r_MinMax = half2(0.0, 5.0);

/// <summary>
/// Emission Speed
/// </summary>
static const half2 Transparent_r1_p1_g_MinMax = half2(0.0, 5.0);

/// <summary>
/// Iridescence Srength
/// </summary>
static const half2 Transparent_r1_p1_b_MinMax = half2(0.0, 3.0);

/// <summary>
/// Iridescence Power
/// </summary>
static const half2 Transparent_r1_p1_a_MinMax = half2(0.0, 5.0);

/// <summary>
/// Detail Gradient Power
/// </summary>
static const half2 Transparent_r1_p2_r_MinMax = half2(0.0, 10.0);

/// <summary>
/// Detail Normal Strength
/// </summary>
static const half2 Transparent_r1_p2_g_MinMax = half2(0.0, 2.0);

/// <summary>
/// Detail Curvature Strength
/// </summary>
static const half2 Transparent_r1_p2_b_MinMax = half2(0.0, 2.0);

/// <summary>
/// Detail Booleans (Bitmask Slot. Use Decode_Bitmask instead of UnpackLUT)
/// </summary>
static const half2 Transparent_r1_p2_a_MinMax = half2(0.0, 255.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Transparent_r1_p3_r_MinMax = half2(0.0, 0.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Transparent_r1_p3_g_MinMax = half2(0.0, 0.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Transparent_r1_p3_b_MinMax = half2(0.0, 0.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Transparent_r1_p3_a_MinMax = half2(0.0, 0.0);

/// <summary>
/// Detail Index
/// </summary>
static const half2 Transparent_r1_p4_g_MinMax = half2(0.0, 64.0);

/// <summary>
/// Emission Mode
/// </summary>
static const half2 Transparent_r1_p4_b_MinMax = half2(0.0, 7.0);

/// <summary>
/// Iridescence Mode
/// </summary>
static const half2 Transparent_r1_p4_a_MinMax = half2(0.0, 8.0);




// --- CRYSTAL RANGES ---

/// <summary>
/// Metallic
/// </summary>
static const half2 Crystal_r1_ca_MinMax = half2(0.0, 1.0);

/// <summary>
/// Smoothness
/// </summary>
static const half2 Crystal_r1_ea_MinMax = half2(0.0, 1.0);

/// <summary>
/// Emission Strength
/// </summary>
static const half2 Crystal_r1_p1_r_MinMax = half2(0.0, 5.0);

/// <summary>
/// Emission Speed
/// </summary>
static const half2 Crystal_r1_p1_g_MinMax = half2(0.0, 5.0);

/// <summary>
/// IOR
/// </summary>
static const half2 Crystal_r1_p1_b_MinMax = half2(1.0, 3.0);

/// <summary>
/// Transmission
/// </summary>
static const half2 Crystal_r1_p1_a_MinMax = half2(0.0, 1.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Crystal_r1_p2_r_MinMax = half2(0.0, 0.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Crystal_r1_p2_g_MinMax = half2(0.0, 0.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Crystal_r1_p2_b_MinMax = half2(0.0, 0.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Crystal_r1_p2_a_MinMax = half2(0.0, 0.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Crystal_r1_p3_r_MinMax = half2(0.0, 0.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Crystal_r1_p3_g_MinMax = half2(0.0, 0.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Crystal_r1_p3_b_MinMax = half2(0.0, 0.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Crystal_r1_p3_a_MinMax = half2(0.0, 0.0);

/// <summary>
/// Prismatic Shift
/// </summary>
static const half2 Crystal_r1_p4_g_MinMax = half2(0.0, 1.0);

/// <summary>
/// Prismatic Power
/// </summary>
static const half2 Crystal_r1_p4_b_MinMax = half2(0.0, 5.0);

/// <summary>
/// Emission Mode
/// </summary>
static const half2 Crystal_r1_p4_a_MinMax = half2(0.0, 7.0);




// --- LIQUID RANGES ---

/// <summary>
/// Metallic
/// </summary>
static const half2 Liquid_r1_ca_MinMax = half2(0.0, 1.0);

/// <summary>
/// Smoothness
/// </summary>
static const half2 Liquid_r1_ea_MinMax = half2(0.0, 1.0);

/// <summary>
/// Noise Scale
/// </summary>
static const half2 Liquid_r1_p1_r_MinMax = half2(0.0, 10.0);

/// <summary>
/// Noise Power
/// </summary>
static const half2 Liquid_r1_p1_g_MinMax = half2(0.0, 5.0);

/// <summary>
/// Distortion Scale
/// </summary>
static const half2 Liquid_r1_p1_b_MinMax = half2(0.0, 10.0);

/// <summary>
/// Distortion Strength
/// </summary>
static const half2 Liquid_r1_p1_a_MinMax = half2(0.0, 5.0);

/// <summary>
/// Phase Speed
/// </summary>
static const half2 Liquid_r1_p2_r_MinMax = half2(0.0, 5.0);

/// <summary>
/// Flow Strength
/// </summary>
static const half2 Liquid_r1_p2_g_MinMax = half2(0.0, 5.0);

/// <summary>
/// Wave Height
/// </summary>
static const half2 Liquid_r1_p2_b_MinMax = half2(0.0, 2.0);

/// <summary>
/// Bubble Speed
/// </summary>
static const half2 Liquid_r1_p2_a_MinMax = half2(0.0, 5.0);

/// <summary>
/// Emission Pattern Scale
/// </summary>
static const half2 Liquid_r1_p3_r_MinMax = half2(0.0, 10.0);

/// <summary>
/// Emission Strength
/// </summary>
static const half2 Liquid_r1_p3_g_MinMax = half2(0.0, 5.0);

/// <summary>
/// Iridescence Strength
/// </summary>
static const half2 Liquid_r1_p3_b_MinMax = half2(0.0, 3.0);

/// <summary>
/// Iridescence Power
/// </summary>
static const half2 Liquid_r1_p3_a_MinMax = half2(0.0, 3.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r1_p4_g_MinMax = half2(0.0, 3.0);

/// <summary>
/// Emission Mode
/// </summary>
static const half2 Liquid_r1_p4_b_MinMax = half2(0.0, 7.0);

/// <summary>
/// Iridescence Mode
/// </summary>
static const half2 Liquid_r1_p4_a_MinMax = half2(0.0, 8.0);

/// <summary>
/// Edge Distance
/// </summary>
static const half2 Liquid_r2_ca_MinMax = half2(0.0, 10.0);

/// <summary>
/// Viscosity
/// </summary>
static const half2 Liquid_r2_ea_MinMax = half2(0.0, 1.0);

/// <summary>
/// Transmission
/// </summary>
static const half2 Liquid_r2_p1_r_MinMax = half2(0.0, 1.0);

/// <summary>
/// Absorption
/// </summary>
static const half2 Liquid_r2_p1_g_MinMax = half2(0.0, 5.0);

/// <summary>
/// IOR
/// </summary>
static const half2 Liquid_r2_p1_b_MinMax = half2(1.0, 2.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r2_p1_a_MinMax = half2(0.0, 3.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r2_p2_r_MinMax = half2(0.0, 3.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r2_p2_g_MinMax = half2(0.0, 3.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r2_p2_b_MinMax = half2(0.0, 3.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r2_p2_a_MinMax = half2(0.0, 3.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r2_p3_r_MinMax = half2(0.0, 3.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r2_p3_g_MinMax = half2(0.0, 3.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r2_p3_b_MinMax = half2(0.0, 3.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r2_p3_a_MinMax = half2(0.0, 3.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r2_p4_r_MinMax = half2(0.0, 3.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r2_p4_g_MinMax = half2(0.0, 3.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r2_p4_b_MinMax = half2(0.0, 3.0);

/// <summary>
/// None (Not defined in schema)
/// </summary>
static const half2 Liquid_r2_p4_a_MinMax = half2(0.0, 3.0);

#endif