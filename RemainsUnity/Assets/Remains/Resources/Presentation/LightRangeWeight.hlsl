#ifndef REMAINS_LIGHT_RANGE_WEIGHT
#define REMAINS_LIGHT_RANGE_WEIGHT

// Fade across the whole reach, with zero value and slope at the outer edge.
// Keep the authored brightness at one metre, or a quarter of a short reach.
float LightRangeWeight(float distanceWorld,float radius)
{
    float window=saturate(1-distanceWorld*distanceWorld/(radius*radius));
    float reference=min(1,radius*.25)/radius;
    float normalise=1-reference*reference;
    return window*window/(normalise*normalise);
}

#endif
