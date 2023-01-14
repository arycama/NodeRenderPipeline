#ifndef SPEEDTREE_WIND_INCLUDED
#define SPEEDTREE_WIND_INCLUDED

float3 UnpackNormalFromFloat(float value)
{
	float3 decodeKey = float3(16.0, 1.0, 0.0625);

    // decode into [0,1] range
	float3 decodedValue = frac(value / decodeKey);

    // move back into [-1,1] range & normalize
	return (decodedValue * 2.0 - 1.0);
}

float4 CubicSmooth(float4 data)
{
	return data * data * (3.0 - 2.0 * data);
}

float4 TriangleWave(float4 data)
{
	return abs((frac(data + 0.5) * 2.0) - 1.0);
}

float4 TrigApproximate(float4 data)
{
	return (CubicSmooth(TriangleWave(data)) - 0.5) * 2.0;
}

float3x3 RotationMatrix(float3 axis, float angle)
{
    // compute sin/cos of angle
	float2 sinCos;
	sincos(angle, sinCos.x, sinCos.y);

	float c = sinCos.y;
	float s = sinCos.x;
	float t = 1.0 - c;
	float x = axis.x;
	float y = axis.y;
	float z = axis.z;

	return float3x3(t * x * x + c, t * x * y - s * z, t * x * z + s * y,
     t * x * y + s * z, t * y * y + c, t * y * z - s * x,
     t * x * z - s * y, t * y * z + s * x, t * z * z + c);
}

float Roll(float current, float maxScale, float minScale, float speed, float ripple, float3 pos, float time, float3 rotatedWindVector)
{
	float windAngle = dot(pos, -rotatedWindVector) * ripple;
	float adjust = TrigApproximate(float4(windAngle + time * speed, 0.0, 0.0, 0.0)).x;
	adjust = (adjust + 1.0) * 0.5;

	return lerp(current * minScale, current * maxScale, adjust);
}

float Twitch(float3 pos, float amount, float sharpness, float time)
{
	float twitchFudge = 0.87;
	float4 oscillations = TrigApproximate(float4(time + (pos.x + pos.z), twitchFudge * time + pos.y, 0.0, 0.0));

    //float twitch = sin(fFreq1 * time + (pos.x + pos.z)) * cos(fFreq2 * time + pos.y);
	float twitch = oscillations.x * oscillations.y * oscillations.y;
	twitch = (twitch + 1.0) * 0.5;

	return amount * pow(saturate(twitch), sharpness);
}

//  This function computes an oscillation value and whip value if necessary.
//  Whip and oscillation are combined like this to minimize calls to
//  TrigApproximate( ) when possible.
float Oscillate(float3 pos, float time, float offset, float weight, float whip, bool hasWhip, bool hasRoll, bool highQuality, float twitch, float twitchFreqScale, inout float4 oscillations, float3 rotatedWindVector, float4 WindVector)
{
	float oscillation = 1.0;
	if (highQuality)
	{
		if (hasWhip)
			oscillations = TrigApproximate(float4(time + offset, time * twitchFreqScale + offset, twitchFreqScale * 0.5 * (time + offset), time + offset + (1.0 - weight)));
		else
			oscillations = TrigApproximate(float4(time + offset, time * twitchFreqScale + offset, twitchFreqScale * 0.5 * (time + offset), 0.0));

		float fineDetail = oscillations.x;
		float broadDetail = oscillations.y * oscillations.z;

		float target = 1.0;
		float amount = broadDetail;
		if (broadDetail < 0.0)
		{
			target = -target;
			amount = -amount;
		}

		broadDetail = lerp(broadDetail, target, amount);
		broadDetail = lerp(broadDetail, target, amount);

		oscillation = broadDetail * twitch * (1.0 - WindVector.w) + fineDetail * (1.0 - twitch);

		if (hasWhip)
			oscillation *= 1.0 + (oscillations.w * whip);
	}
	else
	{
		if (hasWhip)
			oscillations = TrigApproximate(float4(time + offset, time * 0.689 + offset, 0.0, time + offset + (1.0 - weight)));
		else
			oscillations = TrigApproximate(float4(time + offset, time * 0.689 + offset, 0.0, 0.0));

		oscillation = oscillations.x + oscillations.y * oscillations.x;

		if (hasWhip)
			oscillation *= 1.0 + (oscillations.w * whip);
	}

    //if (hasRoll)
    //{
    //  oscillation = Roll(oscillation, WindRollingBranches.x, WindRollingBranches.y, WindRollingBranches.z, WindRollingBranches.w, pos.xyz, time + offset, rotatedWindVector);
    //}

	return oscillation;
}

float Turbulence(float time, float offset, float globalTime, float turbulence)
{
	float turbulenceFactor = 0.1;

	float4 oscillations = TrigApproximate(float4(time * turbulenceFactor + offset, globalTime * turbulence * turbulenceFactor + offset, 0.0, 0.0));

	return 1.0 - (oscillations.x * oscillations.y * oscillations.x * oscillations.y * turbulence);
}

//  This function positions any tree geometry based on their untransformed
//  position and 4 wind floats.
float3 GlobalWind(float3 pos, float3 instancePos, bool preserveShape, float3 rotatedWindVector, float time, float4 windGlobal, float4 windBranchAdherences)
{
    // WIND_LOD_GLOBAL may be on, but if the global wind effect (WIND_EFFECT_GLOBALWind)
    // was disabled for the tree in the Modeler, we should skip it
	float fLength = 1.0;
	if (preserveShape)
		fLength = length(pos.xyz);

    // compute how much the height contributes
	float adjust = max(pos.y - (1.0 / windGlobal.z) * 0.25, 0.0) * windGlobal.z;
	if (adjust != 0.0)
		adjust = pow(abs(adjust), windGlobal.w);

    // primary oscillation
	float4 oscillations = TrigApproximate(float4(instancePos.x + time, instancePos.y + time * 0.8, 0.0, 0.0));
	float fOsc = oscillations.x + (oscillations.y * oscillations.y);
	float fMoveAmount = windGlobal.y * fOsc;

    // move a minimum amount based on direction adherence
	fMoveAmount += windBranchAdherences.x / windGlobal.z;

    // adjust based on how high up the tree this vertex is
	fMoveAmount *= adjust;

    // xy component
	pos.xz += rotatedWindVector.xz * fMoveAmount;
	if (preserveShape)
		pos.xyz = normalize(pos.xyz) * fLength;

	return pos;
}

float3 SimpleBranchWind(float3 pos, float3 instancePos, float weight, float offset, float time, float fDistance, float twitch, float fTwitchScale, float whip, bool hasWhip, bool hasRoll, bool highQuality, float3 rotatedWindVector, float4 windVector)
{
    // turn the offset back into a nearly normalized vector
	float3 vWindVector = UnpackNormalFromFloat(offset);
	vWindVector = vWindVector * weight;

    // try to fudge time a bit so that instances aren't in sync
	time += instancePos.x + instancePos.y;

    // oscillate
	float4 oscillations;
	float fOsc = Oscillate(pos, time, offset, weight, whip, hasWhip, hasRoll, highQuality, twitch, fTwitchScale, oscillations, rotatedWindVector, windVector);

	pos.xyz += vWindVector * fOsc * fDistance;

	return pos;
}

float3 DirectionalBranchWind(float3 pos, float3 instancePos, float weight, float offset, float time, float fDistance, float turbulence, float fAdherence, float twitch, float fTwitchScale, float whip, bool hasWhip, bool hasRoll, bool highQuality, bool bTurbulence, float3 rotatedWindVector, float4 windVector, float4 windAnimation)
{
    // turn the offset back into a nearly normalized vector
	float3 vWindVector = UnpackNormalFromFloat(offset);
	vWindVector = vWindVector * weight;

    // try to fudge time a bit so that instances aren't in sync
	time += instancePos.x + instancePos.y;

    // oscillate
	float4 oscillations;
	float fOsc = Oscillate(pos, time, offset, weight, whip, hasWhip, false, highQuality, twitch, fTwitchScale, oscillations, rotatedWindVector, windVector);

	pos.xyz += vWindVector * fOsc * fDistance;

    // add in the direction, accounting for turbulence
	float fAdherenceScale = 1.0;
	if (bTurbulence)
		fAdherenceScale = Turbulence(time, offset, windAnimation.x, turbulence);

	if (hasWhip)
		fAdherenceScale += oscillations.w * windVector.w * whip;

    //if (hasRoll)
    //  fAdherenceScale = Roll(fAdherenceScale, WindRollingBranches.x, WindRollingBranches.y, WindRollingBranches.z, WindRollingBranches.w, pos.xyz, time + offset, rotatedWindVector);

	pos.xyz += rotatedWindVector * fAdherence * fAdherenceScale * weight;

	return pos;
}

float3 DirectionalBranchWindFrondStyle(float3 pos, float3 instancePos, float weight, float offset, float time, float fDistance, float turbulence, float fAdherence, float twitch, float fTwitchScale, float whip, bool hasWhip, bool hasRoll, bool highQuality, bool bTurbulence, float3 rotatedWindVector, float3 vRotatedBranchAnchor, float4 windVector, float4 windAnimation)
{
    // turn the offset back into a nearly normalized vector
	float3 vWindVector = UnpackNormalFromFloat(offset);
	vWindVector = vWindVector * weight;

    // try to fudge time a bit so that instances aren't in sync
	time += instancePos.x + instancePos.y;

    // oscillate
	float4 oscillations;
	float fOsc = Oscillate(pos, time, offset, weight, whip, hasWhip, false, highQuality, twitch, fTwitchScale, oscillations, rotatedWindVector, windVector);

	pos.xyz += vWindVector * fOsc * fDistance;

    // add in the direction, accounting for turbulence
	float fAdherenceScale = 1.0;
	if (bTurbulence)
		fAdherenceScale = Turbulence(time, offset, windAnimation.x, turbulence);

    //if (hasRoll)
    //  fAdherenceScale = Roll(fAdherenceScale, WindRollingBranches.x, WindRollingBranches.y, WindRollingBranches.z, WindRollingBranches.w, pos.xyz, time + offset, rotatedWindVector);

	if (hasWhip)
		fAdherenceScale += oscillations.w * windVector.w * whip;

	float3 vWindAdherenceVector = vRotatedBranchAnchor - pos.xyz;
	pos.xyz += vWindAdherenceVector * fAdherence * fAdherenceScale * weight;

	return pos;
}

// Apply only to better, best, palm winds
float3 BranchWind(bool isPalmWind, float3 pos, float3 instancePos, float4 vWindData, float3 rotatedWindVector, float3 vRotatedBranchAnchor, float4 windBranchAdherences, float4 WindBranchTwitch, float4 windBranch, float4 windBranchWhip, float4 windTurbulences, float4 windVector, float4 windAnimation)
{
	if (isPalmWind)
	{
		pos = DirectionalBranchWindFrondStyle(pos, instancePos, vWindData.x, vWindData.y, windBranch.x, windBranch.y, windTurbulences.x, windBranchAdherences.y, WindBranchTwitch.x, WindBranchTwitch.y, windBranchWhip.x, true, false, true, true, rotatedWindVector, vRotatedBranchAnchor, windVector, windAnimation);
	}
	else
	{
		pos = SimpleBranchWind(pos, instancePos, vWindData.x, vWindData.y, windBranch.x, windBranch.y, WindBranchTwitch.x, WindBranchTwitch.y, windBranchWhip.x, false, false, true, rotatedWindVector, windVector);
	}

	return pos;
}

float3 Learipple(float3 pos, float3 vDirection, float fScale, float fPackedRippleDir, float time, float amount, bool bDirectional, float fTrigOffset)
{
    // compute how much to move
	float4 vInput = float4(time + fTrigOffset, 0.0, 0.0, 0.0);
	float fMoveAmount = amount * TrigApproximate(vInput).x;

	if (bDirectional)
	{
		pos.xyz += vDirection.xyz * fMoveAmount * fScale;
	}
	else
	{
		float3 vRippleDir = UnpackNormalFromFloat(fPackedRippleDir);
		pos.xyz += vRippleDir * fMoveAmount * fScale;
	}

	return pos;
}

float3 LeafTumble(float3 pos, inout float3 vDirection, float fScale, float3 vAnchor, float3 vGrowthDir, float fTrigOffset, float time, float fFlip, float fTwist, float fAdherence, float3 vTwitch, float4 vRoll, bool bTwitch, bool hasRoll, float3 rotatedWindVector)
{
    // compute all oscillations up front
	float3 vFracs = frac((vAnchor + fTrigOffset) * 30.3);
	float offset = vFracs.x + vFracs.y + vFracs.z;
	float4 oscillations = TrigApproximate(float4(time + offset, time * 0.75 - offset, time * 0.01 + offset, time * 1.0 + offset));

    // move to the origin and get the growth direction
	float3 vOriginPos = pos.xyz - vAnchor;
	float fLength = length(vOriginPos);

    // twist
	float fOsc = oscillations.x + oscillations.y * oscillations.y;
	float3x3 matTumble = RotationMatrix(vGrowthDir, fScale * fTwist * fOsc);

    // with wind
	float3 axis = cross(vGrowthDir, rotatedWindVector);
	float fDot = clamp(dot(rotatedWindVector, vGrowthDir), -1.0, 1.0);
	axis.y += fDot;
	axis = normalize(axis);

	float angle = acos(fDot);

	float fAdherenceScale = 1.0;
    //if (hasRoll)
    //{
    //  fAdherenceScale = Roll(fAdherenceScale, vRoll.x, vRoll.y, vRoll.z, vRoll.w, vAnchor.xyz, time, rotatedWindVector);
    //}

	fOsc = oscillations.y - oscillations.x * oscillations.x;

	float twitch = 0.0;
	if (bTwitch)
		twitch = Twitch(vAnchor.xyz, vTwitch.x, vTwitch.y, vTwitch.z + offset);

	matTumble = mul(matTumble, RotationMatrix(axis, fScale * (angle * fAdherence * fAdherenceScale + fOsc * fFlip + twitch)));

	vDirection = mul(matTumble, vDirection);
	vOriginPos = mul(matTumble, vOriginPos);

	vOriginPos = normalize(vOriginPos) * fLength;

	return (vOriginPos + vAnchor);
}

//  Optimized (for instruction count) version. Assumes leaf 1 and 2 have the same options
float3 LeafWind(bool isBestWind, bool bLeaf2, float3 pos, inout float3 vDirection, float fScale, float3 vAnchor, float fPackedGrowthDir, float fPackedRippleDir, float rippleTrigOffset, float3 rotatedWindVector, float4 WindLeaf1Ripple, float4 WindLeaf2Ripple, float4 WindLeaf1Tumble, float4 WindLeaf2Tumble, float4 WindLeaf1Twitch, float4 WindLeaf2Twitch)
{
	pos = Learipple(pos, vDirection, fScale, fPackedRippleDir, (bLeaf2 ? WindLeaf2Ripple.x : WindLeaf1Ripple.x), (bLeaf2 ? WindLeaf2Ripple.y : WindLeaf1Ripple.y),
     false, rippleTrigOffset);

	if (isBestWind)
	{
		float3 vGrowthDir = UnpackNormalFromFloat(fPackedGrowthDir);
		pos = LeafTumble(pos, vDirection, fScale, vAnchor, vGrowthDir, fPackedGrowthDir,
	   (bLeaf2 ? WindLeaf2Tumble.x : WindLeaf1Tumble.x),
	   (bLeaf2 ? WindLeaf2Tumble.y : WindLeaf1Tumble.y),
	   (bLeaf2 ? WindLeaf2Tumble.z : WindLeaf1Tumble.z),
	   (bLeaf2 ? WindLeaf2Tumble.w : WindLeaf1Tumble.w),
	   (bLeaf2 ? WindLeaf2Twitch.xyz : WindLeaf1Twitch.xyz),
	   0.0f,
	   (bLeaf2 ? true : true),
	   (bLeaf2 ? true : true),
	   rotatedWindVector);
	}

	return pos;
}

float3 RippleFrondOneSided(float3 pos, inout float3 vDirection, float fU, float fV, float rippleScale, float3 vBinormal, float3 vTangent, float4 WindFrondRipple)
{
	float offset = 0.0;
	if (fU < 0.5)
		offset = 0.75;

	float4 oscillations = TrigApproximate(float4((WindFrondRipple.x + fV) * WindFrondRipple.z + offset, 0.0, 0.0, 0.0));

	float amount = rippleScale * oscillations.x * WindFrondRipple.y;
	float3 vOffset = amount * vDirection;
	pos.xyz += vOffset;

	vTangent.xyz = normalize(vTangent.xyz + vOffset * WindFrondRipple.w);
	float3 vNewNormal = normalize(cross(vBinormal.xyz, vTangent.xyz));
	if (dot(vNewNormal, vDirection.xyz) < 0.0)
		vNewNormal = -vNewNormal;
	vDirection.xyz = vNewNormal;

	return pos;
}

float3 RippleFrondTwoSided(float3 pos, inout float3 vDirection, float fU, float fLengthPercent, float fPackedRippleDir, float rippleScale, float3 vBinormal, float3 vTangent, float4 WindFrondRipple)
{
	float4 oscillations = TrigApproximate(float4(WindFrondRipple.x * fLengthPercent * WindFrondRipple.z, 0.0, 0.0, 0.0));
	float3 vRippleDir = UnpackNormalFromFloat(fPackedRippleDir);
	float amount = rippleScale * oscillations.x * WindFrondRipple.y;
	float3 vOffset = amount * vRippleDir;

	pos.xyz += vOffset;

	vTangent.xyz = normalize(vTangent.xyz + vOffset * WindFrondRipple.w);
	float3 vNewNormal = normalize(cross(vBinormal.xyz, vTangent.xyz));
	if (dot(vNewNormal, vDirection.xyz) < 0.0)
		vNewNormal = -vNewNormal;
	vDirection.xyz = vNewNormal;
	return pos;
}

float3 RippleFrond(float3 pos, inout float3 vDirection, float fU, float fV, float fPackedRippleDir, float rippleScale, float fLenghtPercent, float3 vBinormal, float3 vTangent, float4 WindFrondRipple)
{
	return RippleFrondOneSided(pos, vDirection, fU, fV, rippleScale, vBinormal, vTangent, WindFrondRipple);
}

#endif // SPEEDTREE_WIND_INCLUDED