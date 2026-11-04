// Caller supplies PolarSample(bin,distance). Integrate comparisons/transmission, never blocker depths.
// Normalise by represented overlap, not theoretical width: float endpoints lose low bits near angle N.
float PolarBoxFilter(float angle,float width,float distanceWorld)
{
    float low=angle-width*.5,high=angle+width*.5,sum=0,weightSum=0;
    int first=(int)floor(low),last=(int)floor(high);
    [loop] for(int index=first;index<=last;index++)
    {
        float weight=max(0,min(high,index+1)-max(low,index));
        sum+=PolarSample(index,distanceWorld)*weight;weightSum+=weight;
    }
    return sum/max(weightSum,1e-8);
}
