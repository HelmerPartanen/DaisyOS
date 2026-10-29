#include <assert.h>
#include <stdio.h>
#include "edge_geometry.h"
int main(void) {
    for(unsigned side=0;side<4;side++)for(unsigned f=0;f<=100;f++) {
        struct edge_position p={.width=1920,.height=1080};double fraction;
        edge_enter(&p,side,f/100.0);
        assert(p.active&&p.x>=0&&p.x<1920&&p.y>=0&&p.y<1080);
        // Move farther into the destination, then back through its entry edge.
        double dx=side==0?-100:side==1?100:0,dy=side==2?-100:side==3?100:0;
        assert(!edge_move(&p,dx,dy,&fraction));assert(p.active);
        assert(edge_move(&p,-dx*2,-dy*2,&fraction));assert(!p.active&&fraction>=0&&fraction<=1);
        assert(fabs(fraction-f/100.0)<.00001);
    }
    struct edge_position p={.width=1366,.height=768};double fraction;
    edge_enter(&p,1,.5);assert(!edge_move(&p,NAN,0,&fraction));assert(isfinite(p.x));
    for(int i=0;i<10000;i++) { edge_enter(&p,1,.5);assert(!edge_move(&p,5000,5000,&fraction));assert(p.x<=1365&&p.y<=767); }
    puts("PASS: all four edge entries, round trips, 404 boundary positions, non-finite motion and clamping");
    return 0;
}
