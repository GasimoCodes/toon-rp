﻿Shader "Hidden/Toon RP/Outline (Inverted Hull)"
{
	SubShader
	{
	    Pass
		{
		    Name "Toon RP Outline (Inverted Hull)"
			
			HLSLPROGRAM

			#pragma vertex VS
			#pragma fragment PS

			#include "ToonRPInvertedHullOutline.hlsl"

			ENDHLSL
		}
	    
	    Pass
		{
		    Name "Toon RP Outline (Inverted Hull, Custom Normals)"
			
			HLSLPROGRAM

			#pragma vertex VS
			#pragma fragment PS

			#define TOON_RP_USE_TEXCOORD2_NORMALS
			#include "ToonRPInvertedHullOutline.hlsl"

			ENDHLSL
		}
	}
}