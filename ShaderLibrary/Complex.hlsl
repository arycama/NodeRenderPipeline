// Complex number utilities

#ifndef COMPLEX_INCLUDED
#define COMPLEX_INCLUDED

struct complex
{
	float r;
	float i;
};

complex czero()
{
	complex result = { 0.0, 0.0 };
	return result;
}

// Initialize a complex number with only a real (Setting imaginary to zero)
complex creal(float r)
{
	complex result = { r, 0.0 };
	return result;
}

// Initialize a complex number with only an imaginary number (Setting the real to zero)
complex cimg(float i)
{
	complex result = { 0.0, i };
	return result;
}

complex cadd(complex c0, complex c1)
{
	complex result = { c0.r + c1.r, c0.i + c1.i };
	return result;
}

complex csub(complex c0, complex c1)
{
	complex result = { c0.r - c1.r, c0.i - c1.i };
	return result;
}

complex cmul(complex c0, complex c1)
{
	complex result = { c0.r * c1.r - c0.i * c1.i, c0.r * c1.i + c0.i * c1.r };
	return result;
}

complex conj(complex c)
{
	complex result = { c.r, -c.i };
	return result;
}

complex cexp(complex c)
{
	complex result;
	result.r = cos(c.i) * exp(c.r);
	result.i = sin(c.i) * exp(c.r);
	return result;
}

// https://cirosantilli.com/complex-dot-product
complex cdot(complex a, complex b)
{
	complex result;
	result.r = a.r * b.r + a.i * b.i;
	result.i = a.i * b.r - a.r * b.i;
	return result;
}

#endif